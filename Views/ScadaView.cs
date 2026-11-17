// IEDExplorer - lightweight IEC 61850 FAT dashboard.
// GPL-3.0-or-later, consistent with the surrounding application.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using WeifenLuo.WinFormsUI.Docking;

namespace IEDExplorer.Views
{
    /// <summary>
    /// A read-only-by-default SCADA/FAT overview. Values come from the existing
    /// IEDExplorer MMS model (reads and reports); no second MMS session is opened.
    /// IEC 61850 controls are delegated to the existing Select/Operate dialog.
    /// </summary>
    public sealed class ScadaView : DockContent
    {
        private sealed class SignalCard
        {
            internal string Address;
            internal string Caption;
            internal bool IsCommand;
            internal NodeData Node;
            internal string LastValue;
            internal DateTime LastChanged;
            internal Panel Panel;
            internal Label Value;
            internal Label Detail;
            internal Button Control;
        }

        private readonly WindowManager manager;
        private readonly FlowLayoutPanel surface;
        private readonly Label connectionLabel;
        private readonly ToolStripButton allowControls;
        private readonly ToolStripButton automaticReads;
        private readonly Timer refreshTimer;
        private readonly List<SignalCard> cards = new List<SignalCard>();
        private Iec61850State boundIed;
        private string profileHost = "";
        private int nextRead;

        private static readonly Color Ink = Color.FromArgb(29, 48, 71);
        private static readonly Color Muted = Color.FromArgb(111, 128, 145);
        private static readonly Color Blue = Color.FromArgb(32, 99, 188);
        private static readonly Color Background = Color.FromArgb(245, 248, 252);

        public ScadaView(WindowManager windowManager)
        {
            manager = windowManager;
            Text = "SCADA / FAT monitor";
            TabText = "SCADA / FAT";
            ShowHint = DockState.Document;
            BackColor = Background;

            var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8, 4, 8, 4) };
            toolbar.Items.Add(MakeButton("Read now", delegate { ReadVisibleCards(int.MaxValue); }));
            automaticReads = new ToolStripButton("Auto read") { Checked = true, CheckOnClick = true, ToolTipText = "Cycle MMS reads for monitored values; reports also update the model." };
            toolbar.Items.Add(automaticReads);
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(MakeButton("Save layout", SaveLayout));
            toolbar.Items.Add(MakeButton("Open layout", OpenLayout));
            toolbar.Items.Add(new ToolStripSeparator());
            allowControls = new ToolStripButton("Enable controls") { CheckOnClick = true, ForeColor = Color.Firebrick };
            allowControls.CheckedChanged += delegate
            {
                if (allowControls.Checked && MessageBox.Show(this,
                    "Enable IEC 61850 commands for this FAT session?\n\n" +
                    "A separate confirmation and the existing command dialog will still be required for every operation.",
                    "Enable controls", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    allowControls.Checked = false;
                RefreshCards();
            };
            toolbar.Items.Add(allowControls);

            connectionLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 35,
                Padding = new Padding(20, 9, 0, 0),
                BackColor = Color.FromArgb(231, 238, 247),
                ForeColor = Ink,
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                Text = "Connect to an IED, then drag signals from the left tree to this area."
            };

            surface = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Background,
                Padding = new Padding(12),
                AllowDrop = true
            };
            surface.DragEnter += OnDragEnter;
            surface.DragDrop += OnDragDrop;
            Controls.Add(surface);
            Controls.Add(connectionLabel);
            Controls.Add(toolbar);

            refreshTimer = new Timer { Interval = 1500 };
            refreshTimer.Tick += delegate { RefreshFromModel(); };
            refreshTimer.Start();
        }

        private static ToolStripButton MakeButton(string title, Action action)
        {
            var button = new ToolStripButton(title) { DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += delegate { action(); };
            return button;
        }

        private static bool IsControlNode(NodeBase node)
        {
            if (node == null) return false;
            NodeBase ctl = node.Name == "ctlVal" ? node : node.Parent;
            if (ctl == null || ctl.Name != "ctlVal" || ctl.Parent == null) return false;
            return ctl.Parent.Name == "Oper" || ctl.Parent.Name == "SBO" || ctl.Parent.Name == "SBOw";
        }

        private static bool IsLeaf(NodeBase node)
        {
            return node is NodeData && !(node is NodeDO) && node.isLeaf();
        }

        /// <summary>Called by the IEC/MMS tree's context menu or the drop target.</summary>
        public void AddSignal(NodeBase node, bool asCommand)
        {
            if (!IsLeaf(node))
            {
                MessageBox.Show(this, "Select an individual leaf data attribute (for example stVal, mag.f or Oper.ctlVal.i).",
                    "Select a signal", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (asCommand && !IsControlNode(node))
            {
                MessageBox.Show(this, "Only IEC 61850 Oper/SBO/SBOw ctlVal data attributes can be added as commands.",
                    "Unsupported command", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var state = manager.mainWindow.Get_iecf();
            if (state == null || state.DataModel == null)
            {
                MessageBox.Show(this, "Connect to the IED and load its MMS model before adding signals.",
                    "No IED", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!string.IsNullOrEmpty(profileHost) &&
                !string.Equals(profileHost, state.hostname, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "This layout belongs to " + profileHost +
                    ". Open a matching IED or create a new layout before adding signals.",
                    "IED mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CommAddress address = node.CommAddress;
            if (string.IsNullOrEmpty(address.Domain) || string.IsNullOrEmpty(address.Variable))
                return;
            string key = address.Domain + "/" + address.Variable;
            if (cards.Any(x => x.Address == key && x.IsCommand == asCommand)) return;
            if (cards.Count >= 200)
            {
                MessageBox.Show(this, "The FAT dashboard is limited to 200 signal cards.");
                return;
            }
            if (string.IsNullOrEmpty(profileHost)) profileHost = state.hostname;
            var card = new SignalCard { Address = key, Caption = node.IecAddress, IsCommand = asCommand, Node = (NodeData)node };
            cards.Add(card);
            CreateCard(card);
            RefreshCards();
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(NodeData)) ? DragDropEffects.Link : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            NodeBase node = e.Data.GetData(typeof(NodeData)) as NodeBase;
            if (node == null) return;
            // Controls must be deliberately selected, never silently made operable by a drop.
            if (IsControlNode(node))
            {
                DialogResult choice = MessageBox.Show(this,
                    "Add this ctlVal attribute as an IEC 61850 command?\n\nYes: command tile (disabled by default)\nNo: read-only tile",
                    "Signal type", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel) return;
                AddSignal(node, choice == DialogResult.Yes);
            }
            else AddSignal(node, false);
        }

        private void CreateCard(SignalCard card)
        {
            var tile = new Panel
            {
                Width = 256,
                Height = 174,
                BackColor = Color.White,
                Margin = new Padding(9),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(14),
                AllowDrop = true
            };
            tile.DragEnter += OnDragEnter;
            tile.DragDrop += OnDragDrop;

            var caption = new Label
            {
                Text = card.Caption,
                Left = 13, Top = 12, Width = 225, Height = 40,
                ForeColor = Ink, Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoEllipsis = true
            };
            var value = new Label
            {
                Text = card.IsCommand ? "COMMAND" : "—",
                Left = 13, Top = 57, Width = 225, Height = 43,
                ForeColor = Blue, Font = new Font("Segoe UI", 20, FontStyle.Bold),
                AutoEllipsis = true
            };
            var detail = new Label
            {
                Text = card.Address,
                Left = 13, Top = 112, Width = 225, Height = 44,
                ForeColor = Muted, Font = new Font("Segoe UI", 8),
                AutoEllipsis = true
            };
            tile.Controls.Add(caption);
            tile.Controls.Add(value);
            tile.Controls.Add(detail);
            card.Panel = tile;
            card.Value = value;
            card.Detail = detail;

            if (card.IsCommand)
            {
                value.Visible = false;
                var button = new Button
                {
                    Text = "Select / Operate...",
                    Left = 13, Top = 61, Width = 225, Height = 38,
                    BackColor = Color.FromArgb(232, 240, 252),
                    ForeColor = Ink,
                    FlatStyle = FlatStyle.Flat,
                    Enabled = false
                };
                button.Click += delegate { ExecuteCommand(card); };
                tile.Controls.Add(button);
                card.Control = button;
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add("Rename tile", null, delegate { RenameCard(card, caption); });
            menu.Items.Add("Remove tile", null, delegate
            {
                cards.Remove(card);
                surface.Controls.Remove(tile);
                tile.Dispose();
            });
            tile.ContextMenuStrip = menu;
            caption.ContextMenuStrip = menu;
            value.ContextMenuStrip = menu;
            detail.ContextMenuStrip = menu;
            if (card.Control != null) card.Control.ContextMenuStrip = menu;
            surface.Controls.Add(tile);
        }

        private void RenameCard(SignalCard card, Label caption)
        {
            using (var dialog = new Form { Text = "Tile name", Width = 420, Height = 150, FormBorderStyle = FormBorderStyle.FixedDialog,
                                           StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false })
            {
                var edit = new TextBox { Left = 14, Top = 12, Width = 375, Text = card.Caption };
                var ok = new Button { Text = "OK", Left = 297, Top = 48, Width = 90, DialogResult = DialogResult.OK };
                dialog.AcceptButton = ok;
                dialog.Controls.Add(edit);
                dialog.Controls.Add(ok);
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(edit.Text))
                {
                    card.Caption = edit.Text.Trim();
                    caption.Text = card.Caption;
                }
            }
        }

        private bool IsOnline(out Iec61850State state)
        {
            state = manager.mainWindow.Get_iecf();
            return state != null &&
                   state.DataModel != null &&
                   state.DataModel.ied != null &&
                   state.tstate == TcpProtocolState.TCP_CONNECTED &&
                   state.ostate == IsoProtocolState.OSI_CONNECTED &&
                   state.istate == Iec61850lStateEnum.IEC61850_FREILAUF &&
                   !string.IsNullOrEmpty(state.hostname);
        }

        private void RefreshFromModel()
        {
            if (IsDisposed || Disposing) return;
            Iec61850State state;
            bool online = IsOnline(out state);
            bool matchingHost = online && (string.IsNullOrEmpty(profileHost) ||
                string.Equals(profileHost, state.hostname, StringComparison.OrdinalIgnoreCase));
            if (!matchingHost) state = null;

            if (!object.ReferenceEquals(boundIed, state))
            {
                boundIed = state;
                foreach (SignalCard card in cards)
                {
                    card.Node = state == null ? null : state.DataModel.ied.FindNodeByAddress(card.Address) as NodeData;
                    card.LastValue = null;
                    card.LastChanged = DateTime.MinValue;
                }
            }
            connectionLabel.Text = !online ? "OFFLINE  •  Connect an IEC 61850 IED to start monitoring"
                : !matchingHost ? "IED MISMATCH  •  Layout is bound to " + profileHost + "; connected to " + state.hostname
                : "MMS CONNECTED  •  " + state.hostname + "  •  " + cards.Count + " configured items";
            connectionLabel.ForeColor = matchingHost ? Color.FromArgb(15, 105, 69) : Color.FromArgb(153, 65, 40);

            RefreshCards();
            if (matchingHost && automaticReads.Checked) ReadVisibleCards(6);
        }

        private void RefreshCards()
        {
            Iec61850State state;
            bool online = IsOnline(out state) &&
                          (string.IsNullOrEmpty(profileHost) ||
                           string.Equals(profileHost, state.hostname, StringComparison.OrdinalIgnoreCase));
            foreach (SignalCard card in cards)
            {
                if (card.Control != null)
                    card.Control.Enabled = online && allowControls.Checked && card.Node != null;
                if (card.IsCommand)
                {
                    card.Detail.Text = card.Address + (online ? "\nCommand requires confirmation" : "\nOffline / unbound");
                    continue;
                }

                if (!online || card.Node == null)
                {
                    card.Value.Text = card.Node == null && online ? "NOT FOUND" : "OFFLINE";
                    card.Value.ForeColor = Muted;
                    card.Detail.Text = card.Address;
                    continue;
                }

                string current = card.Node.DataValue == null ? "—" : card.Node.StringValue;
                if (!string.Equals(current, card.LastValue, StringComparison.Ordinal))
                {
                    card.LastValue = current;
                    card.LastChanged = DateTime.Now;
                }
                card.Value.Text = current;
                card.Value.ForeColor = Blue; // No inferred open/closed/tripped semantics from bare booleans.
                card.Detail.Text = card.Address + "\n" +
                    (card.LastChanged == DateTime.MinValue ? "No value received" :
                     "Value changed " + card.LastChanged.ToString("HH:mm:ss"));
            }
        }

        private void ReadVisibleCards(int limit)
        {
            Iec61850State state;
            if (!IsOnline(out state) || boundIed != state) return;
            if (!string.IsNullOrEmpty(profileHost) &&
                !string.Equals(profileHost, state.hostname, StringComparison.OrdinalIgnoreCase)) return;
            var readable = cards.Where(x => !x.IsCommand && x.Node != null).ToArray();
            if (readable.Length == 0) return;
            int count = Math.Min(limit, readable.Length);
            for (int i = 0; i < count; i++)
            {
                if (state.SendQueue.Count > 30) break; // Do not flood the MMS worker.
                var node = readable[nextRead % readable.Length].Node;
                nextRead++;
                state.Send(new NodeBase[] { node }, node.CommAddress, ActionRequested.Read);
            }
        }

        private void ExecuteCommand(SignalCard card)
        {
            Iec61850State state;
            if (!card.IsCommand || !allowControls.Checked || !IsOnline(out state) ||
                boundIed != state || card.Node == null || !IsControlNode(card.Node) ||
                !string.Equals(profileHost, state.hostname, StringComparison.OrdinalIgnoreCase)) return;

            if (MessageBox.Show(this,
                "Open IEC 61850 command dialog for:\n" + card.Address +
                "\n\nCheck the selected IED and switchgear before proceeding.",
                "Confirm command target", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            manager.ExecuteScadaCommand(card.Node);
        }

        private void SaveLayout()
        {
            using (var dialog = new SaveFileDialog { Filter = "IEDExplorer SCADA layout (*.scada.xml)|*.scada.xml|XML (*.xml)|*.xml",
                                                    DefaultExt = "scada.xml", FileName = "FAT-layout.scada.xml" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    new XDocument(new XElement("ScadaLayout",
                        new XAttribute("version", "1"), new XAttribute("host", profileHost ?? ""),
                        cards.Select(x => new XElement("Card",
                            new XAttribute("address", x.Address),
                            new XAttribute("caption", x.Caption),
                            new XAttribute("kind", x.IsCommand ? "command" : "value"))))).Save(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save layout: " + ex.Message, "Save failed",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OpenLayout()
        {
            using (var dialog = new OpenFileDialog { Filter = "IEDExplorer SCADA layout (*.scada.xml;*.xml)|*.scada.xml;*.xml" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    XDocument xml = XDocument.Load(dialog.FileName);
                    XElement root = xml.Root;
                    if (root == null || root.Name != "ScadaLayout" || (string)root.Attribute("version") != "1")
                        throw new InvalidDataException("Unsupported SCADA profile format");
                    var newCards = root.Elements("Card").ToList();
                    if (newCards.Count > 200) throw new InvalidDataException("Too many signals in profile");
                    var loaded = new List<SignalCard>();
                    foreach (XElement element in newCards)
                    {
                        string address = (string)element.Attribute("address");
                        string caption = (string)element.Attribute("caption");
                        string kind = (string)element.Attribute("kind");
                        if (string.IsNullOrEmpty(address) || address.Length > 300 ||
                            !address.Contains("/") || string.IsNullOrEmpty(caption) || caption.Length > 200 ||
                            (kind != "value" && kind != "command"))
                            throw new InvalidDataException("Invalid SCADA card");
                        loaded.Add(new SignalCard { Address = address, Caption = caption, IsCommand = kind == "command" });
                    }
                    foreach (Control old in surface.Controls.Cast<Control>().ToList())
                    {
                        surface.Controls.Remove(old);
                        old.Dispose();
                    }
                    cards.Clear();
                    cards.AddRange(loaded);
                    profileHost = (string)root.Attribute("host") ?? "";
                    boundIed = null;
                    allowControls.Checked = false;
                    foreach (SignalCard card in cards) CreateCard(card);
                    RefreshFromModel();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not open layout: " + ex.Message, "Invalid layout",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refreshTimer.Stop();
            refreshTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}

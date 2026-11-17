// IEDExplorer GPL-3.0-or-later
// Breaker model discovery: a binding always names the actual MMS paths; never infer
// a control by choosing an arbitrary signal or a similar looking IEC 61850 name.
using System;
using System.Globalization;
using System.Linq;

namespace IEDExplorer.Views
{
    internal enum BreakerPosition { Unknown, Intermediate, Open, Closed, Bad }

    internal sealed class ScadaBreakerBinding
    {
        internal string Domain;
        internal string LogicalNode;
        internal string StatusAddress;
        internal string QualityAddress;
        internal string TimestampAddress;
        internal string ControlModelAddress;
        internal string OperateAddress;
        internal string SelectWithValueAddress;
        internal string SelectAddress;
        internal string OpenEnabledAddress;
        internal string CloseEnabledAddress;
        internal string OpenBlockedAddress;
        internal string CloseBlockedAddress;
        internal string SelectedAddress;

        internal NodeData Position;
        internal NodeData Quality;
        internal NodeData Timestamp;
        internal NodeData ControlModel;
        internal NodeData Operate;
        internal NodeData SelectWithValue;
        internal NodeData Select;
        internal NodeData OpenEnabled;
        internal NodeData CloseEnabled;
        internal NodeData OpenBlocked;
        internal NodeData CloseBlocked;
        internal NodeData Selected;

        internal string Key { get { return Domain + "/" + LogicalNode; } }

        internal static bool IsBreakerCandidate(NodeBase node)
        {
            if (node == null) return false;
            NodeBase ancestor = node;
            bool position = false;
            while (ancestor != null)
            {
                if (ancestor.Name == "Pos") position = true;
                if (ancestor is NodeLN)
                    return IsBreakerLogicalNode(ancestor.Name) &&
                        (position || node == ancestor || node is NodeDO);
                ancestor = ancestor.Parent;
            }
            return false;
        }

        internal static bool IsBreakerLogicalNode(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("CSWI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("XCBR", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static ScadaBreakerBinding FromNode(NodeBase selected, NodeIed mmsRoot)
        {
            if (selected == null || mmsRoot == null || !IsBreakerCandidate(selected))
                return null;
            NodeBase ln = selected;
            while (ln != null && !(ln is NodeLN)) ln = ln.Parent;
            if (ln == null || ln.Parent == null) return null;
            string domain = ln.Parent.Name;
            string lnName = ln.Name;
            ScadaBreakerBinding binding = new ScadaBreakerBinding
            {
                Domain = domain, LogicalNode = lnName
            };
            binding.Rebind(mmsRoot);
            if (binding.Position == null) return null;
            return binding;
        }

        internal static ScadaBreakerBinding FromKey(string key, NodeIed mmsRoot)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOf('/') <= 0 || mmsRoot == null)
                return null;
            var parts = key.Split(new[] {'/'}, 2);
            if (!IsBreakerLogicalNode(parts[1]) ||
                parts[1].Contains("$") || parts[1].Contains(".") || parts[1].Length > 80)
                return null;
            ScadaBreakerBinding binding = new ScadaBreakerBinding
            {
                Domain = parts[0], LogicalNode = parts[1]
            };
            binding.Rebind(mmsRoot);
            return binding.Position == null ? null : binding;
        }

        private static NodeData Look(NodeIed root, string path)
        {
            return root.FindNodeByAddress(path) as NodeData;
        }

        private static string Path(string domain, string ln, string fc, string obj, string attribute)
        {
            return domain + "/" + ln + "$" + fc + "$" + obj + "$" + attribute;
        }

        internal void Rebind(NodeIed mmsRoot)
        {
            StatusAddress = Path(Domain, LogicalNode, "ST", "Pos", "stVal");
            QualityAddress = Path(Domain, LogicalNode, "ST", "Pos", "q");
            TimestampAddress = Path(Domain, LogicalNode, "ST", "Pos", "t");
            ControlModelAddress = Path(Domain, LogicalNode, "CF", "Pos", "ctlModel");
            OperateAddress = Path(Domain, LogicalNode, "CO", "Pos", "Oper$ctlVal");
            SelectWithValueAddress = Path(Domain, LogicalNode, "CO", "Pos", "SBOw$ctlVal");
            SelectAddress = Path(Domain, LogicalNode, "CO", "Pos", "SBO");
            SelectedAddress = Path(Domain, LogicalNode, "ST", "stSeld", "stVal");

            Position = Look(mmsRoot, StatusAddress);
            Quality = Look(mmsRoot, QualityAddress);
            Timestamp = Look(mmsRoot, TimestampAddress);
            ControlModel = Look(mmsRoot, ControlModelAddress);
            Operate = Look(mmsRoot, OperateAddress);
            SelectWithValue = Look(mmsRoot, SelectWithValueAddress);
            Select = Look(mmsRoot, SelectAddress);
            Selected = Look(mmsRoot, SelectedAddress);

            // Optional ABB-specific status qualifiers: same LD and matching function index.
            // Never use these to pick a control target: all commands remain within the
            // explicitly chosen CSWI/XCBR logical node.
            OpenEnabledAddress = CloseEnabledAddress = null;
            OpenBlockedAddress = CloseBlockedAddress = null;
            OpenEnabled = CloseEnabled = OpenBlocked = CloseBlocked = null;
            if (LogicalNode.IndexOf("CSWI", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string suffix = new string(LogicalNode.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                if (suffix.Length > 0)
                {
                    string ilo = "CBCILO" + suffix;
                    string xcb = "CBXCBR" + suffix;
                    OpenEnabledAddress = Path(Domain, ilo, "ST", "EnaOpn", "stVal");
                    CloseEnabledAddress = Path(Domain, ilo, "ST", "EnaCls", "stVal");
                    OpenBlockedAddress = Path(Domain, xcb, "ST", "BlkOpn", "stVal");
                    CloseBlockedAddress = Path(Domain, xcb, "ST", "BlkCls", "stVal");
                    OpenEnabled = Look(mmsRoot, OpenEnabledAddress);
                    CloseEnabled = Look(mmsRoot, CloseEnabledAddress);
                    OpenBlocked = Look(mmsRoot, OpenBlockedAddress);
                    CloseBlocked = Look(mmsRoot, CloseBlockedAddress);
                }
            }
        }

        internal static BreakerPosition ParsePosition(object value)
        {
            if (value == null || value is bool) return BreakerPosition.Unknown;
            long number;
            try
            {
                byte[] bytes = value as byte[];
                if (bytes != null)
                {
                    // MMS BIT STRING (SIZE 2) is packed MSB-first; e.g. 0x40=open, 0x80=closed.
                    if (bytes.Length != 1 || (bytes[0] & 0x3F) != 0)
                        return BreakerPosition.Unknown;
                    number = (bytes[0] >> 6) & 0x03;
                }
                else
                    number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception) { return BreakerPosition.Unknown; }
            if (number == 0) return BreakerPosition.Intermediate;
            if (number == 1) return BreakerPosition.Open;
            if (number == 2) return BreakerPosition.Closed;
            if (number == 3) return BreakerPosition.Bad;
            return BreakerPosition.Unknown;
        }

        internal static byte[] EncodeDpc(int target)
        {
            if (target != 1 && target != 2)
                throw new ArgumentOutOfRangeException("target", "IEC DPC command must be open=1 or closed=2");
            return new byte[] { (byte)(target << 6) };
        }

        internal BreakerPosition PositionValue
        {
            get { return ParsePosition(Position == null ? null : Position.DataValue); }
        }

        internal bool QualityGood
        {
            get
            {
                // IEC 61850 Quality bit-string: first two most-significant bits encode validity.
                // Invalid/questionable/reserved or no quality => no SCADA command.
                if (Quality == null || !(Quality.DataValue is byte[])) return false;
                byte[] bytes = (byte[])Quality.DataValue;
                return bytes.Length >= 2 &&
                       (bytes[0] & 0xC0) == 0 &&
                       // failure, oldData, inconsistent, substituted, test are not good
                       (bytes[0] & 0x1F) == 0 &&
                       (bytes[1] & 0x18) == 0;
            }
        }

        internal static int ParseControlModel(object value)
        {
            if (value == null || value is bool) return -1;
            try
            {
                int model = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return model >= 0 && model <= 4 ? model : -1;
            }
            catch (Exception) { return -1; }
        }

        internal int ControlModelValue
        {
            get { return ParseControlModel(ControlModel == null ? null : ControlModel.DataValue); }
        }

        // DPC indications use 1=off and 2=on; IEC single-point may be Boolean.
        // If a relevant blocking/permission point is available but has not yet
        // been read, inhibit commands until its meaning can be established.
        private static bool TryGetActive(NodeData signal, out bool active)
        {
            active = false;
            if (signal == null || signal.DataValue == null) return false;
            if (signal.DataValue is bool)
            {
                active = (bool)signal.DataValue;
                return true;
            }
            try
            {
                byte[] bits = signal.DataValue as byte[];
                if (bits != null)
                {
                    BreakerPosition dp = ParsePosition(bits);
                    if (dp != BreakerPosition.Open && dp != BreakerPosition.Closed) return false;
                    active = dp == BreakerPosition.Closed;
                    return true;
                }
                long raw = Convert.ToInt64(signal.DataValue, CultureInfo.InvariantCulture);
                if (raw != 1 && raw != 2) return false;
                active = raw == 2;
                return true;
            }
            catch (Exception) { return false; }
        }

        internal bool CanCommand(int targetValue)
        {
            if (targetValue != 1 && targetValue != 2 || !QualityGood) return false;
            BreakerPosition position = PositionValue;
            if (position != BreakerPosition.Open && position != BreakerPosition.Closed) return false;
            if ((position == BreakerPosition.Open && targetValue == 1) ||
                (position == BreakerPosition.Closed && targetValue == 2)) return false;
            int model = ControlModelValue;
            // Only the 2-bit IEC DPC control type is accepted here, with a real
            // 2-bit Pos status read and either direct (1) or SBOw (4) control model.
            if (Position == null || Position.DataType != scsm_MMS_TypeEnum.bit_string ||
                !(Position.DataParam is int) || (int)Position.DataParam != 6)
                return false;
            if (Operate == null || !Operate.isLeaf() ||
                Operate.DataType != scsm_MMS_TypeEnum.bit_string)
                return false;
            if (model == 4 && (SelectWithValue == null || !SelectWithValue.isLeaf() ||
                SelectWithValue.DataType != scsm_MMS_TypeEnum.bit_string))
                return false;
            if (model != 1 && model != 4) return false;

            NodeData enabled = targetValue == 1 ? OpenEnabled : CloseEnabled;
            NodeData blocked = targetValue == 1 ? OpenBlocked : CloseBlocked;
            bool active;
            if (enabled != null && (!TryGetActive(enabled, out active) || !active)) return false;
            if (blocked != null && (!TryGetActive(blocked, out active) || active)) return false;
            return true;
        }

        internal string ControlSummary
        {
            get
            {
                int model = ControlModelValue;
                if (model < 0) return "ctlModel ej avläst";
                if (model == 0) return "Endast status (ctlModel 0)";
                if (model == 1) return "Direkt manöver (ctlModel 1)";
                if (model == 2) return "SBO normal – endast övervakning";
                if (model == 4) return "SBOw / förbättrad säkerhet (Select + Operate)";
                if (model == 3) return "Enhanced direct – endast övervakning";
                return "Okänd manövermodell";
            }
        }

        internal string QualitySummary
        {
            get
            {
                if (Quality == null || Quality.DataValue == null) return "Kvalitet saknas";
                return QualityGood ? "Kvalitet god" : "Kvalitet ogiltig/test/osäker";
            }
        }

        internal NodeData[] MonitoredNodes()
        {
            return new[] { Position, Quality, Timestamp, ControlModel, OpenEnabled, CloseEnabled,
                           OpenBlocked, CloseBlocked, Selected }.Where(x => x != null).Distinct().ToArray();
        }
    }
}

// IEDExplorer GPL-3.0-or-later
// Fast deterministic checks that do not open a socket or send any IEC commands.
using System;
using IEDExplorer.Views;

namespace IEDExplorer
{
    internal static class ScadaBreakerSelfTest
    {
        private static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Breaker self-test: " + message);
        }

        private static NodeBase Add(NodeBase parent, NodeBase child)
        {
            return parent.AddChildNode(child);
        }

        internal static void Run()
        {
            Iec61850State state = new Iec61850State();
            NodeIed root = state.DataModel.ied;
            NodeBase ln = Add(Add(root, new NodeLD("CTRL")), new NodeLN("CBCSWI1"));

            NodeBase posSt = Add(Add(ln, new NodeFC("ST")), new NodeDO("Pos"));
            NodeData st = (NodeData)Add(posSt, new NodeData("stVal") { DataType = scsm_MMS_TypeEnum.integer, DataValue = (long)1 });
            NodeData q = (NodeData)Add(posSt, new NodeData("q") { DataType = scsm_MMS_TypeEnum.bit_string, DataValue = new byte[] { 0, 0 } });
            Add(posSt, new NodeData("t") { DataType = scsm_MMS_TypeEnum.utc_time });

            NodeBase posCf = Add(Add(ln, new NodeFC("CF")), new NodeDO("Pos"));
            NodeData model = (NodeData)Add(posCf, new NodeData("ctlModel")
            { DataType = scsm_MMS_TypeEnum.integer, DataValue = (long)1 });

            NodeBase posCo = Add(Add(ln, new NodeFC("CO")), new NodeDO("Pos"));
            NodeBase oper = Add(posCo, new NodeData("Oper"));
            NodeData ctl = (NodeData)Add(oper,
                new NodeData("ctlVal") { DataType = scsm_MMS_TypeEnum.integer, DataValue = (long)2 });

            ScadaBreakerBinding b = ScadaBreakerBinding.FromNode(ctl, root);
            Check(b != null, "cannot infer breaker from Oper.ctlVal");
            Check(b.Position == st && b.Quality == q && b.Operate == ctl, "wrong automatically connected paths");
            Check(b.ControlModel == model, "missing control model");
            Check(b.Key == "CTRL/CBCSWI1", "wrong IED identity");
            Check(b.PositionValue == BreakerPosition.Open, "1 must mean open");
            Check(b.QualityGood, "valid IEC quality rejected");
            Check(!b.CanCommand(2), "non-DPC integer control must be blocked from the symbol");
            Check(!b.CanCommand(1), "cannot re-open already open device");
            Check(ScadaBreakerBinding.FromKey(b.Key, root) != null, "profile reload failed");
            Check(ScadaBreakerBinding.ParsePosition((long)0) == BreakerPosition.Intermediate, "transition");
            Check(ScadaBreakerBinding.ParsePosition((long)2) == BreakerPosition.Closed, "2 must mean closed");
            Check(ScadaBreakerBinding.ParsePosition((long)3) == BreakerPosition.Bad, "3 must mean bad");
            Check(ScadaBreakerBinding.ParsePosition(true) == BreakerPosition.Unknown, "boolean must not be guessed");
            Check(ScadaBreakerBinding.ParsePosition(null) == BreakerPosition.Unknown, "missing value");
            Check(ScadaBreakerBinding.ParsePosition(new byte[] { 0x00 }) == BreakerPosition.Intermediate, "Dbpos 00");
            Check(ScadaBreakerBinding.ParsePosition(new byte[] { 0x40 }) == BreakerPosition.Open, "Dbpos 01");
            Check(ScadaBreakerBinding.ParsePosition(new byte[] { 0x80 }) == BreakerPosition.Closed, "Dbpos 10");
            Check(ScadaBreakerBinding.ParsePosition(new byte[] { 0xC0 }) == BreakerPosition.Bad, "Dbpos 11");
            Check(ScadaBreakerBinding.ParsePosition(new byte[] { 0x41 }) == BreakerPosition.Unknown, "malformed Dbpos");
            Check(ScadaBreakerBinding.EncodeDpc(1)[0] == 0x40, "encode open DPC");
            Check(ScadaBreakerBinding.EncodeDpc(2)[0] == 0x80, "encode close DPC");
            Check(ScadaBreakerBinding.FromNode(st, root) != null, "cannot infer from Pos.stVal");

            st.DataValue = (long)2;
            Check(b.PositionValue == BreakerPosition.Closed, "status must reflect feedback");
            Check(!b.CanCommand(1) && !b.CanCommand(2), "non-DPC control must be blocked");
            q.DataValue = new byte[] { 0x40, 0x00 };
            Check(!b.QualityGood && !b.CanCommand(1), "bad quality must inhibit commands");
            q.DataValue = new byte[] { 0x00, 0x00 };

            model.DataValue = (long)4;
            Check(b.ControlModelValue == 4 && !b.CanCommand(1), "non-DPC SBO must not be accepted");

            // Retarget the synthetic IED to the real REX615 DPC control type.
            st.DataType = scsm_MMS_TypeEnum.bit_string;
            st.DataValue = new byte[] { 0x80 };
            st.DataParam = 6;
            ctl.DataType = scsm_MMS_TypeEnum.boolean;
            ctl.DataValue = true;
            NodeBase sbow = Add(posCo, new NodeData("SBOw"));
            NodeData sbowCtl = (NodeData)Add(sbow, new NodeData("ctlVal")
                { DataType = scsm_MMS_TypeEnum.boolean, DataValue = true });
            b.Rebind(root);
            Check(b.PositionValue == BreakerPosition.Closed, "REX status from bit string");
            Check(b.SelectWithValue == sbowCtl && b.CanCommand(1), "SBOw boolean close->open must work");
            Check(!b.CanCommand(2), "SBOw must not re-close closed breaker");
            model.DataValue = (long)1;
            Check(b.CanCommand(1), "direct DPC control should work");
            model.DataValue = (long)4;
            model.DataValue = (long)0;
            Check(!b.CanCommand(1), "status-only must inhibit controls");
            model.DataValue = null;
            Check(!b.CanCommand(1), "unknown control model must inhibit controls");
            model.DataValue = (long)1;
            st.DataValue = (long)0;
            Check(!b.CanCommand(1), "intermediate position must inhibit controls");
            st.DataValue = (long)3;
            Check(!b.CanCommand(1), "bad position must inhibit controls");

            // Explicitly reject unrelated or missing logical nodes.
            Check(ScadaBreakerBinding.FromKey("CTRL/OTHER1", root) == null, "invalid LN was accepted");
            Check(ScadaBreakerBinding.FromKey("CTRL/CBCSWI2", root) == null, "missing LN was accepted");

            Console.WriteLine("SCADA breaker self-test PASS (read-only; no network)");
        }
    }
}

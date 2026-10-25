using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

static class PatchModpack {
    const string SRC = @"C:\Users\86749\AppData\Local\Temp\pclil\pcl.exe";
    const string DST = @"C:\Users\86749\AppData\Local\Temp\pclil\pcl_fixed.exe";

    static void Main() {
        ModuleDefMD mod = ModuleDefMD.Load(SRC);
        Console.WriteLine("version: " + mod.Assembly.Version);

        TypeDef t = mod.Find("PCL.ModModpack", false);
        if (t == null) { Console.WriteLine("ERR: type not found"); return; }

        MethodDef m = null;
        foreach (MethodDef c in t.Methods)
            if (c.Name == "ModpackInstall" && c.ParamDefs.Count == 3) m = c;
        if (m == null) { Console.WriteLine("ERR: method not found"); return; }
        Console.WriteLine("method static=" + m.IsStatic + " il=" + m.Body.Instructions.Count);

        CilBody body = m.Body;
        IList<Instruction> ins = body.Instructions;

        int idx = -1;
        for (int i = 0; i < ins.Count; i++) if (ins[i].Offset == 0x2DA) { idx = i; break; }
        if (idx < 0) { Console.WriteLine("ERR: anchor not found"); return; }
        for (int k = 0; k < 3; k++)
            if (ins[idx + k].OpCode.Code != Code.Nop) { Console.WriteLine("ERR: anchor+" + k + " not nop"); return; }
        Console.WriteLine("anchor idx=" + idx + " next=" + ins[idx + 3].OpCode.Code);

        Local archiveVar = body.Variables[1];
        Local baseVar = body.Variables[2];
        Local packVar = body.Variables[4];
        Console.WriteLine("vars: " + archiveVar.Type.FullName + " | " + baseVar.Type.FullName + " | " + packVar.Type.FullName);

        Local comboVar = new Local(m.ReturnType);
        body.Variables.Add(comboVar);
        Console.WriteLine("combo var: " + comboVar.Type.FullName);

        List<KeyValuePair<int, string>> table = new List<KeyValuePair<int, string>>();
        table.Add(new KeyValuePair<int, string>(3, "InstallPackMCBBS"));
        table.Add(new KeyValuePair<int, string>(2, "InstallPackMMC"));
        table.Add(new KeyValuePair<int, string>(4, "InstallPackModrinth"));
        table.Add(new KeyValuePair<int, string>(0, "InstallPackCurseForge"));
        table.Add(new KeyValuePair<int, string>(1, "InstallPackHMCL"));
        table.Add(new KeyValuePair<int, string>(9, "InstallPackLauncherPack"));

        MethodDef mcArg0 = m;
        Func<string, MethodDef> findInstaller = delegate(string name) {
            foreach (MethodDef c in t.Methods) if (c.Name == name) return c;
            return null;
        };
        Func<string, Instruction> argIns = delegate(string pname) {
            if (pname == "FileAddress") return Instruction.Create(OpCodes.Ldarg, mcArg0.Parameters[0]);
            if (pname == "Archive") return Instruction.Create(OpCodes.Ldloc, archiveVar);
            if (pname == "ArchiveBaseFolder") return Instruction.Create(OpCodes.Ldloc, baseVar);
            if (pname == "InstanceName") return Instruction.Create(OpCodes.Ldarg, mcArg0.Parameters[1]);
            if (pname == "Logo") return Instruction.Create(OpCodes.Ldarg, mcArg0.Parameters[2]);
            throw new Exception("unexpected param " + pname);
        };

        List<List<Instruction>> blocks = new List<List<Instruction>>();
        List<Instruction> heads = new List<Instruction>();
        List<int> values = new List<int>();
        foreach (KeyValuePair<int, string> kv in table) {
            MethodDef md = findInstaller(kv.Value);
            if (md == null) { Console.WriteLine("ERR: no " + kv.Value); return; }
            List<string> names = new List<string>();
            foreach (ParamDef pd in md.ParamDefs) names.Add(pd.Name.ToString());
            Console.WriteLine("  " + kv.Value + "(" + string.Join(",", names.ToArray()) + ")");
            List<Instruction> blk = new List<Instruction>();
            foreach (string n in names) blk.Add(argIns(n));
            blk.Add(Instruction.Create(OpCodes.Call, md));
            blk.Add(Instruction.Create(OpCodes.Stloc, comboVar));
            blocks.Add(blk);
            heads.Add(blk[0]);
            values.Add(kv.Key);
        }

        MethodDef cmp = findInstaller("InstallPackCompress");
        if (cmp == null) { Console.WriteLine("ERR: no InstallPackCompress"); return; }
        List<string> cnames = new List<string>();
        foreach (ParamDef pd in cmp.ParamDefs) cnames.Add(pd.Name.ToString());
        Console.WriteLine("  InstallPackCompress(" + string.Join(",", cnames.ToArray()) + ")");
        List<Instruction> defBlk = new List<Instruction>();
        foreach (string n in cnames) defBlk.Add(argIns(n));
        defBlk.Add(Instruction.Create(OpCodes.Call, cmp));
        defBlk.Add(Instruction.Create(OpCodes.Stloc, comboVar));

        List<Instruction> disp = new List<Instruction>();
        for (int i = 0; i < table.Count; i++) {
            disp.Add(Instruction.Create(OpCodes.Ldloc, packVar));
            disp.Add(Instruction.Create(OpCodes.Ldc_I4, values[i]));
            disp.Add(Instruction.Create(OpCodes.Beq, heads[i]));
        }
        disp.Add(Instruction.Create(OpCodes.Br, defBlk[0]));

        Instruction afterLd = Instruction.Create(OpCodes.Ldloc, comboVar);
        Instruction afterRet = Instruction.Create(OpCodes.Ret);
        Instruction leaveOut = Instruction.Create(OpCodes.Leave, afterLd);

        List<Instruction> gen = new List<Instruction>();
        gen.AddRange(disp);
        for (int i = 0; i < blocks.Count; i++) {
            gen.AddRange(blocks[i]);
            gen.Add(Instruction.Create(OpCodes.Leave, afterLd));
        }
        gen.AddRange(defBlk);
        gen.Add(leaveOut);
        Console.WriteLine("generated: " + gen.Count + " instructions");

        Instruction[] victims = new Instruction[] { ins[idx], ins[idx + 1], ins[idx + 2] };
        ins.RemoveAt(idx); ins.RemoveAt(idx); ins.RemoveAt(idx);
        for (int i = gen.Count - 1; i >= 0; i--) ins.Insert(idx, gen[i]);

        Instruction dispHead = gen[0];
        foreach (Instruction x in ins)
            foreach (Instruction v in victims)
                if (object.ReferenceEquals(x.Operand, v)) x.Operand = dispHead;
        foreach (ExceptionHandler h in body.ExceptionHandlers)
            foreach (Instruction v in victims) {
                if (object.ReferenceEquals(h.TryStart, v)) h.TryStart = dispHead;
                if (object.ReferenceEquals(h.TryEnd, v)) h.TryEnd = dispHead;
                if (object.ReferenceEquals(h.HandlerStart, v)) h.HandlerStart = dispHead;
                if (object.ReferenceEquals(h.HandlerEnd, v)) h.HandlerEnd = dispHead;
                if (object.ReferenceEquals(h.FilterStart, v)) h.FilterStart = dispHead;
            }

        ins.Add(afterLd);
        ins.Add(afterRet);
        foreach (ExceptionHandler h in body.ExceptionHandlers)
            if (h.HandlerEnd == null) h.HandlerEnd = afterLd;

        body.UpdateInstructionOffsets();
        Console.WriteLine("maxStack before: " + body.MaxStack);
        if (body.MaxStack < 8) body.MaxStack = 8;
        Console.WriteLine("maxStack after: " + body.MaxStack);
        int rets = 0, calls = 0;
        foreach (Instruction x in ins) {
            if (x.OpCode.Code == Code.Ret) rets++;
            IMethod im = x.Operand as IMethod;
            if (x.OpCode.Code == Code.Call && im != null && im.Name.String.StartsWith("InstallPack")) calls++;
        }
        Console.WriteLine("offets ok, count=" + ins.Count + " rets=" + rets + " installerCalls=" + calls);
        foreach (ExceptionHandler h in body.ExceptionHandlers)
            Console.WriteLine("  handler " + h.HandlerType + " try=" + h.TryStart.Offset + ".." + h.TryEnd.Offset +
                " handler=" + h.HandlerStart.Offset + ".." + (h.HandlerEnd == null ? -1L : (long)h.HandlerEnd.Offset));

        HashSet<Instruction> set = new HashSet<Instruction>(ins);
        int bad = 0;
        foreach (Instruction x in ins) {
            Instruction tg = x.Operand as Instruction;
            if (tg != null && !set.Contains(tg)) { bad++; Console.WriteLine("  BAD TARGET from " + x.OpCode.Code + " -> " + tg.Offset); }
        }
        Console.WriteLine("bad targets: " + bad);

        mod.Write(DST);
        Console.WriteLine("written " + DST + " size=" + new FileInfo(DST).Length);
    }
}

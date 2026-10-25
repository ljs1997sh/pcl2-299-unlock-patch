using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

// 回退 P4：它把资源版本列表的点击处理器从 Save_Click（下载）换成了 Install_Click
// （整合包自动安装），而且第二个委托用错了类型导致 Castclass 崩溃。
// 这里把 P4 注入的三条指令改回 Nop，并把被替换掉的 ldnull 还原，
// 行为回到官方：点版本项 = 下载该资源。
static class Rollback4 {
    static void Main(string[] a) {
        string src = a[0];
        string dst = a[1];
        ModuleDefMD mod = ModuleDefMD.Load(src);

        TypeDef page = mod.Find("PCL.PageDownloadResourceDetail", false);
        if (page == null) { Console.WriteLine("ABORT: page type not found"); return; }
        MethodDef init = null;
        foreach (MethodDef m in page.Methods) if (m.Name == "InitializeStackResourceVersions") init = m;
        if (init == null) { Console.WriteLine("ABORT: method not found"); return; }

        IList<Instruction> ins = init.Body.Instructions;
        int i = -1;
        for (int k = 0; k < ins.Count; k++) {
            if (ins[k].OpCode.Code != Code.Ldftn) continue;
            MethodDef t = ins[k].Operand as MethodDef;
            if (t != null && t.Name == "Install_Click") { i = k; break; }
        }
        if (i < 1 || i + 5 >= ins.Count) { Console.WriteLine("ABORT: Install_Click ldftn not found"); return; }

        Console.WriteLine("pattern at " + (i - 1) + ".." + (i + 5) + ":");
        for (int k = i - 1; k <= i + 5; k++)
            Console.WriteLine("  [" + k + "] " + ins[k].OpCode.Code + " " + (ins[k].Operand == null ? "" : ins[k].Operand.ToString()));

        if (ins[i - 1].OpCode.Code != Code.Ldarg_0) { Console.WriteLine("ABORT: expected ldarg.0 before"); return; }
        if (ins[i + 1].OpCode.Code != Code.Newobj) { Console.WriteLine("ABORT: expected newobj after"); return; }
        if (ins[i + 2].OpCode.Code != Code.Ldarg_0) { Console.WriteLine("ABORT: expected ldarg.0 second"); return; }
        if (ins[i + 3].OpCode.Code != Code.Ldftn) { Console.WriteLine("ABORT: expected ldftn Save_Click"); return; }
        if (ins[i + 4].OpCode.Code != Code.Newobj) { Console.WriteLine("ABORT: expected newobj Save"); return; }
        if (ins[i + 5].OpCode.Code != Code.Nop) { Console.WriteLine("ABORT: expected nop (original ldnull)"); return; }

        // 1) 抹掉 P4 插入的三条（ldarg.0 / ldftn Install_Click / newobj）
        for (int k = i - 1; k <= i + 1; k++) { ins[k].OpCode = OpCodes.Nop; ins[k].Operand = null; }
        // 2) 把被 P4 换成 Nop 的 ldnull 还原
        ins[i + 5].OpCode = OpCodes.Ldnull;
        ins[i + 5].Operand = null;

        Console.WriteLine("after:");
        for (int k = i - 1; k <= i + 5; k++)
            Console.WriteLine("  [" + k + "] " + ins[k].OpCode.Code + " " + (ins[k].Operand == null ? "" : ins[k].Operand.ToString()));

        mod.Write(dst);
        Console.WriteLine("written " + dst);
    }
}

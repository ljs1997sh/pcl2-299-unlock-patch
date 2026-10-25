using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

static class FinalCheck {
    static void Main(string[] a) {
        var mod = ModuleDefMD.Load(a[0]);
        Console.WriteLine("程序集版本: " + mod.Assembly.Version.ToString());

        var t = mod.Find("PCL.ModModpack", false);
        MethodDef mi = null;
        foreach (var m in t.Methods) if (m.Name == "ModpackInstall" && m.ParamDefs.Count == 3) mi = m;
        var ins = mi.Body.Instructions;
        int rets = 0, calls = 0;
        foreach (var x in ins) {
            if (x.OpCode.Code == Code.Ret) rets++;
            var im = x.Operand as IMethod;
            if (x.OpCode.Code == Code.Call && im != null && im.Name.String.StartsWith("InstallPack")) calls++;
        }
        var set = new HashSet<Instruction>(ins);
        int bad = 0;
        foreach (var x in ins) {
            var tg = x.Operand as Instruction;
            if (tg != null && !set.Contains(tg)) bad++;
        }
        Console.WriteLine("[P2 整合包] 指令 " + ins.Count + " / ret " + rets + " / InstallPack调用 " + calls + " / 越界分支 " + bad + " / maxStack " + mi.Body.MaxStack);

        var page = mod.Find("PCL.PageDownloadResourceDetail", false);
        MethodDef init = null;
        foreach (var m in page.Methods) if (m.Name == "InitializeStackResourceVersions") init = m;
        int ldf = 0;
        foreach (var x in init.Body.Instructions) if (x.OpCode.Code == Code.Ldftn) ldf++;
        Console.WriteLine("[P4 资源页] ldftn 数量: " + ldf);

        var sec = mod.Find("PCL.ModSecret", false);
        var f = sec.FindField("RestrictMcDownload");
        Console.WriteLine("[P6 限制常量] RestrictMcDownload = " + (f != null && f.HasConstant ? f.Constant.Value.ToString() : "?"));

        var pdi = mod.Find("PCL.PageDownloadInstall", false);
        MethodDef li = null;
        foreach (var m in pdi.Methods) if (m.Name == "LoaderInit") li = m;
        bool gate = false;
        foreach (var x in li.Body.Instructions) {
            var s = x.Operand as string;
            if (x.OpCode.Code == Code.Ldstr && s != null && s.Length == 4) gate = true;
        }
        Console.WriteLine("[P1 下载页] 4字门控字符串还在: " + gate);

        int n = 0;
        foreach (var m in t.Methods) if (m.Name.StartsWith("InstallPack") && m.HasBody) n++;
        Console.WriteLine("[安装器] 完整方法数: " + n);
    }
}

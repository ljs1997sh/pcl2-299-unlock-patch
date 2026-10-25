using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

static class Scan {
    static string SigOf(MethodDef m) {
        System.Text.StringBuilder sb = new System.Text.StringBuilder("(");
        MethodSig ms = m.MethodSig;
        if (ms != null) {
            bool first = true;
            foreach (TypeSig t in ms.Params) { if (!first) sb.Append(", "); sb.Append(t.FullName); first = false; }
        }
        sb.Append(") -> ").Append(ms == null ? "?" : ms.RetType.FullName);
        return sb.ToString();
    }

    static string OpText(Instruction x) {
        if (x.Operand == null) return "";
        IMethod im = x.Operand as IMethod;
        if (im != null) return im.DeclaringType.FullName + "::" + im.Name;
        return x.Operand.ToString();
    }

    static void Main(string[] a) {
        ModuleDefMD mod = ModuleDefMD.Load(a[0]);
        int hits = 0;
        foreach (TypeDef t in mod.GetTypes()) {
            foreach (MethodDef m in t.Methods) {
                if (!m.HasBody) continue;
                IList<Instruction> ins = m.Body.Instructions;
                for (int i = 0; i < ins.Count; i++) {
                    if (ins[i].OpCode.Code != Code.Ldftn) continue;
                    MethodDef target = ins[i].Operand as MethodDef;
                    if (target == null) continue;
                    if (target.Name != "Save_Click" && target.Name != "Install_Click") continue;
                    hits++;
                    Console.WriteLine("### " + t.FullName + "::" + m.Name + "  idx=" + i);
                    Console.WriteLine("    target: " + target.Name + " " + SigOf(target));
                    for (int k = Math.Max(0, i - 3); k <= Math.Min(ins.Count - 1, i + 3); k++) {
                        Console.WriteLine("      " + (k == i ? ">" : " ") + " " + k + ": " + ins[k].OpCode.Code + "  " + OpText(ins[k]));
                    }
                }
            }
        }
        Console.WriteLine("total ldftn hits: " + hits);
    }
}

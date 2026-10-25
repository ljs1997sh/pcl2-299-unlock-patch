# PCL 2.99.0.0 合规限制解除补丁（非官方）

> 基于官方 **PCL 2.99.0.0 (413)** 的二进制补丁，注销编译期写死的「MC 下载 / 整合包安装」封锁。
> 仅改动 IL，不改 UI、不加壳、不联网。**不含任何用户数据。**

---

## ⬇️ 只想要能用的版本？下一个文件就够

# [`out/PCL-2.99.0.0-unlocked.exe`](out/PCL-2.99.0.0-unlocked.exe)

**5.7 MB（5,972,992 字节）** —— 把 `out` 文件夹里这一个 exe 下载下来，覆盖你原来的 `Plain Craft Launcher 2.exe`，就能用。

> 其余文件（`src/` 补丁源码、`DISCLAIMER.md`、`LICENSE`）只有在你**想知道改了什么、想自己复现、想核对安全性**时才需要看。
> 覆盖前记得先把自己的原版 exe 改个名备份，随时能换回去。

---

## 0. 声明：本仓库的改动全部由 AI 生成

**`out/` 里的二进制、`src/` 里的全部补丁源码、以及这份文档，都是 AI 写的。**

- 生成环境：DeepSeek Harness 的 AI agent 会话（2026-10-25 ~ 10-26）
- 手法：用 dnlib 直接读写 .NET IL，**没有**反编译成源码再重编译
- 人类做的：提出需求、提供基准二进制、跑运行时验证（启动 / 下载页 / 拖拽整合包 / 资源页点击）
- AI 做的：反汇编定位、判定「分发代码被编译期常量裁掉」、重建 IL、写自检与隐私扫描工具、整理文档

逐条说明、风险与责任划分见 **[DISCLAIMER.md](DISCLAIMER.md)**。

---

## 1. 为什么会有这个补丁

官方 2.99.0.0 在源码里写了一行：

```vb
Public Const RestrictMcDownload As Boolean = True
```

这不是设置项，是**编译期常量**。因为它是 `Const True`，编译器把 `If RestrictMcDownload Then Throw ...` 之后的分发代码全部当**不可达代码**删掉了 —— 于是：

| 功能 | 官方 2.99.0.0 的表现 |
|---|---|
| 下载页版本列表 | 加载器直接抛 `为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能` |
| 把整合包 zip 拖进窗口 | `ModpackInstall` 里只剩「识别包类型 → 抛异常」，**没有任何 `InstallPack*` 调用**；异常被上层静默吞掉，最后弹「PCL 无法确定适合的操作」 |
| 资源页整合包「安装」按钮 | 被条件编译裁掉 |
| 修改组件页的 Minecraft 卡片 | 隐藏 |

补丁做的就是把这些门口的门闩拆掉，并把被编译掉的调用补回去。

## 2. 改了哪些地方

| 编号 | 位置 | 改动 |
|---|---|---|
| **P1** | `PCL.PageDownloadInstall.LoaderInit` | 把「关闭下载」的 LoaderTask 换成真正的 `ModDownload.DlClientListLoader` |
| **P2** | `PCL.ModModpack.ModpackInstall(String,String,String)` | **整段重建分发逻辑**（详见下节） |
| **P4** | `PCL.PageDownloadResourceDetail.InitializeStackResourceVersions` | 还原为官方形态 `ToListItem(Save_Click, Nothing, IsBadDisplay)` |
| **P5** | `PCL.ModModpack.ModpackInstall()`（0 参数重载） | 改写提示文案与颜色 |
| **P6** | `PCL.Settings..cctor` | `SystemSystemUpdate` 默认值改为 3（关闭更新提示），避免自动更新把补丁打回去 |

### P2 细节：被编译掉的分发逻辑

原方法的 IL 长这样（补丁前）：

```
IL_0000  …检测 mcbbs.packmeta / mmc-pack.json / modrinth.index.json / manifest.json / modpack.json / modpack.zip…
        所有检测分支 leave 到同一处
IL_02DA  ldstr "为遵守相关法律法规……"
         newobj Exception(string)
         throw                    ← 方法到此结束，没有 ret
```

补丁后用 dnlib 重建为：

```
IL_02DA  ldloc PackType
         ldc.i4 3  → beq  InstallPackMCBBS
         ldc.i4 2  → beq  InstallPackMMC
         ldc.i4 4  → beq  InstallPackModrinth
         ldc.i4 0  → beq  InstallPackCurseForge
         ldc.i4 1  → beq  InstallPackHMCL
         ldc.i4 9  → beq  InstallPackLauncherPack
         br        InstallPackCompress      （未知类型，按普通压缩包解压）
   每个分支：压参数 → call → stloc → leave 到 finally 之后
   finally：dispose ZipArchive
   之后：ldloc 结果 → ret
```

参数按每个安装器**真实的 `ParamDefs` 名字**映射（`FileAddress` / `Archive` / `ArchiveBaseFolder` / `InstanceName` / `Logo`），不是猜的位置。

自检输出（`src/final-check.cs`）：

```
[P2 整合包] 指令 318 / ret 1 / InstallPack调用 7 / 越界分支 0 / maxStack 5
[P4 资源页] 指向 Save_Click 的 ldftn: 1（Install_Click 注入已清除）
[P1 下载页] 4 字门控字符串还在: False
[安装器] 完整方法数: 7
```

## 3. 关于隐私

**这个 exe 里没有任何使用者的个人数据。**

PCL 把账号、登录态、设置、存档全部放在 `PCL\` 目录和 `.minecraft\` 里，不写进可执行文件。本补丁只在 IL 层把被编译期常量裁掉的功能接回去，**没有新增任何写入、上报或联网行为** —— `src/` 里每一处改动都能对着自检工具逐条核对。

**想自己核验**：PCL 的字符串全部存放在 .NET 的 UTF-16 `#US` 堆里，把 `out/PCL-2.99.0.0-unlocked.exe` 按 UTF-16LE 解码，用任意十六进制工具搜你关心的词即可。除 PCL 自身的界面文案、模组名词典外，只会搜到两类东西：

| 内容 | 来源 |
|---|---|
| `C:\Users\alios\…`、`C:\Users\n\…` | 第三方库构建时留下的 PDB 路径（CacheCow、libwebp-net），上游原版同样带有 |
| `E:\Projects\PCL\…` × 41 | 上游作者编译时 `CallerFilePath` 宏展开的源码路径，官方原版同样带有 |

两类都是**构建信息**，与任何使用者无关。

## 4. 用法

1. 备份你原来的 `Plain Craft Launcher 2.exe`（改名放旁边即可）
2. 用 `out/PCL-2.99.0.0-unlocked.exe` 覆盖原文件
3. 启动 → 下载页应能看到完整版本列表；把整合包 zip 拖进窗口应出现「整合包安装」任务

> exe 改了 IL，**官方签名失效**（`Get-AuthenticodeSignature` 会是 `NotSigned`），SmartScreen 或杀软可能提示「未知发布者」。这是二进制被修改后的必然结果。

## 5. 自己复现

需要 .NET Framework 4.x（Windows 自带）+ [dnlib](https://github.com/0xd4d/dnlib)。

```bat
:: 1) 下载页 / 资源页 / 更新提示（PowerShell，内含 P1/P3/P5/P6）
powershell -NoProfile -ExecutionPolicy Bypass -File src\patch-portable.ps1 PlainCraftLauncher2.exe

:: 2) 重建整合包分发（C#，P2）
csc /r:dnlib.dll /out:patch-modpack.exe src\patch-modpack.cs
patch-modpack.exe

:: 3) 资源页回退到官方形态（C#，P4）
csc /r:dnlib.dll /out:rollback4.exe src\rollback4.cs
rollback4.exe <输入.exe> <输出.exe>

:: 4) 自检
csc /r:dnlib.dll /out:final-check.exe src\final-check.cs
final-check.exe <输出.exe>
```

`patch-portable.ps1` 会先校验程序集版本必须是 `2.99.0.0`，不是就直接退出、绝不动文件。

## 6. 许可与署名

- 本仓库**只包含补丁源码**，不含 PCL 的源代码。
- PCL 本体及其相关知识产权的权利归原作者所有，分发受限，详见 [`LICENSE-UPSTREAM.txt`](LICENSE-UPSTREAM.txt)（《PCL 分发有限许可》）。
- 本项目是**非官方补丁**，与 PCL 作者无关；请勿用任何可能被误认为官方版本的名称发布。
- 上游：<https://github.com/Meloong-Git/PCL> ｜ 官网：<https://meloong.com/pcl>

**本仓库的两层许可：**

| 部分 | 许可 |
|---|---|
| `src/` 下的补丁源码与文档 | **MIT**，见 [`LICENSE`](LICENSE) —— 随便抄、随便改、随便再分发 |
| `out/` 下的二进制 | 是对 PCL 的修改产物，沿用上游《PCL 分发有限许可》，本仓库不额外授权 |

## 6.5 这件事的背景（2026-10-20）

上游为什么关掉下载，值得写清楚，免得后来的人以为这是“作者偷懒”：

- 2026-10-20，PCL2 发布 2.99.0.0，同时关闭 MC 下载功能；更新说明注明“为遵守相关法律法规”，并特别声明**与网易、微软、原作团队无关**。
- 同期 HMCL 等多款国内第三方 MC 启动器也陆续收缩下载服务。
- 社群流传的起因：有未成年人在某个 MC **私服**充值，家长发现后将私服与 PCL2 一并举报（该说法无官方通报佐证）。
- 客观背景：MC 国际版在国内没有版号，第三方启动器提供本体下载本身处于灰色地带。

**本补丁只把被编译期常量裁掉的代码路径接回来，不涉及任何授权校验。** 是否使用、如何分发，由使用者自己判断和承担。

## 7. 已知限制

- 只对 **2.99.0.0 (413)** 有效，别的版本补丁会拒绝执行（版本校验）。
- PCL 启动时会尝试拉 `notice.cfg`，开源/合规构建没有 CDN 签名，日志里会有一条 **403 Forbidden** —— 无害，不影响使用。
- 「检查更新」会被 P6 关掉；想升级得先换回原版 exe → 更新 → 重新打补丁。
- 资源页整合包的「一键安装」按钮**保持官方行为**（点击版本项 = 下载文件），自动安装请用拖拽。

---

**改动全部可逆**：保持一份原版 exe，随时换回去。

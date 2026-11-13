# CmdPal GoldenDict · 本地词典

PowerToys Command Palette 的离线 MDX 词头候选扩展。输入时在插件自己的词头索引中补全、纠错，回车后交给已安装的 GoldenDict-ng 展示释义。

当前版本 **2.2.0**。支持中英文紧凑设置页及 EXE 安装向导。[English](README.md) · [下载与版本记录](https://github.com/mshtang/cmdpal-goldendict/releases)。

**当前发布的是未签名的开发版安装包，需要已开启 Windows 开发者模式。安装器只检查，不修改系统设置。**

## EXE 安装、升级与卸载

1. 准备 Windows 11 x64、Command Palette 和 GoldenDict-ng，并先在 GoldenDict-ng 配好词典。
2. 从 [Releases](https://github.com/mshtang/cmdpal-goldendict/releases) 下载 `CmdPal-GoldenDict-2.2.0-dev-Setup-x64.exe`，双击安装。无需另装 .NET 运行时；开发版未签名，Windows 可能提示未知发布者或 SmartScreen。
3. 在 Palette 运行 **Reload**，打开「本地词典设置 / Local Dictionary Settings」，粘贴程序和 MDX 路径，保存即可。

以后运行新版安装器即可覆盖升级，已有配置与缓存会保留；降级会被阻止。卸载时在 Windows 已安装的应用中选择 **CmdPal GoldenDict (Development installer)**，安装器会注销扩展并清理程序文件，保留用户配置与缓存，不影响 GoldenDict-ng 或原始词典。

安装错误日志：`%LOCALAPPDATA%\CmdPalGoldenDict\Logs\installer.log`。当前仅发布已验证的 x64 构建，ARM64 尚未进行硬件验证。安装包和公开仓库都不包含本机配置、词典或缓存；更新需从 Releases 手动下载。

以下为源码构建、配置格式和匹配机制的详细说明。安装器构建和贡献流程见 [CONTRIBUTING.md](CONTRIBUTING.md)。

本项目不读取 GoldenDict-ng 的私有索引，不接管其词典配置，不读取词条正文或 MDD，不添加网络、发音、翻译或划词功能。

## 当前交付

- `artifacts/win-x64/`：已构建的自包含 Windows x64 扩展，不要求安装 .NET 运行时。
- `config.example.json`：通用的英语/德语多分组配置示例。
- `src/`、`tests/`、`scripts/`：完整源码、测试、构建和本地注册脚本。

运行要求：Windows 11、支持 SDK 0.12 的较新 PowerToys Command Palette、GoldenDict-ng。开发注册需要 Windows 已允许开发包注册。脚本不会更改 Windows 安全设置。

## 使用

如果扩展已在本机注册，在 Command Palette 中执行 `Reload`，再搜索并进入 **本地词典**。

首次安装预编译版本（在项目目录的 PowerShell 中执行）：

```powershell
.\scripts\Install.ps1
```

注册之后，**不要移动或删除 `artifacts/win-x64`**。本地开发包直接引用这个目录。需要移动时，在新位置重新执行安装脚本。

扩展页面会显示已载入词头数。输入例如：

- `appl`：前缀补全。
- `apple`：精确匹配排在最前。
- `aple`、`appel`：拼写纠错。

方向键选择候选，Enter 在 GoldenDict-ng 主窗口中打开；“直接查询”保留原始输入。Tab 可利用宿主提供的词头补全操作。词典名称显示在候选的副标题中，相同拼写的词头合并为一项。

查询以编码后的 `goldendict://...` 字符串作为 exe 的单个参数发送，并显式指定主窗口。这不依赖系统注册 URL 协议，也能兼容本次安装的较旧 GoldenDict-ng。所有参数通过 .NET ArgumentList 传递，不经过 cmd/PowerShell 解释。

在 Command Palette 设置中，可以为顶层“本地词典”命令指定别名或单独的全局快捷键。扩展不抢占或自行注册系统快捷键。

右侧分组过滤器切换候选词库。候选的上下文菜单可打开设置、重新加载或强制重建缓存。首次配置为空、文件不存在、词典无法解析和 GoldenDict 路径无效，均会显示可操作的说明。

## 配置

在 Palette 中搜索 **本地词典设置**，或者从扩展的设置入口、查词页上下文菜单进入同一个原生设置页。

英文界面的命令名称为 **Local Dictionary** 和 **Local Dictionary Settings**。设置页的 **界面语言 / Interface language** 提供「跟随系统 / Follow system」「English」「简体中文」三个选项，保存后立即更新表单、顶层命令、菜单、候选提示和扩展自带的错误提示，无需重启。默认跟随 Windows 显示语言：中文使用简体中文，其余语言使用英文。系统管理的包名称跟随 Windows 语言。

词典条目、词典名称、分组名称、文件路径和 GoldenDict 分组名称属于用户内容，切换语言不会翻译或重命名这些内容。现有配置未填写语言时等同于跟随系统。

页面直接展示现有配置：

- GoldenDict-ng 程序路径、默认分组和搜索选项。
- 每个分组的名称，以及对应的 GoldenDict 分组名称。
- 每本词典的名称、MDX 路径和启用开关。
- 添加/移除词典、添加/移除分组，以及保存、重新载入按钮。

在路径框中直接粘贴文件路径即可。例如 `C:\Dictionaries\Oxford’s Dictionary.mdx` 或 `C:/Dictionaries/Oxford’s Dictionary.mdx`。无需自己把反斜杠写成双反斜杠；从资源管理器复制得到的外层引号会自动去除。空格、中文、撇号、`&`、`#` 和方括号均会保留。也支持 UNC 路径、环境变量和相对配置目录的路径。

增删操作暂存在设置页，点击 **保存** 后生效。保存只处理配置，不扫描或预检查词典文件；后续词头索引按正常后台流程更新。程序路径留空、文件暂时不在本机，也可以先保存。分组名称、重复词典路径和数字范围仍需符合配置结构。

设置页不会启动另一个窗口，不提供文件选择器、自动发现程序或词典预检查。原有 JSON 格式保持兼容，未知字段会保留。保存采用原子替换并保留上一份 `.bak`；如果配置在其他地方被修改，会提示先重新载入，避免覆盖。

默认配置位置：`%LOCALAPPDATA%\PaletteDictionary\config.json`。

默认缓存位置：同目录的 `cache/`。开发或便携运行可通过环境变量 `PALETTEDICTIONARY_CONFIG` 指定另一份完整配置路径，缓存随该配置存放。

保存配置后约 3 秒自动重新加载。MDX 文件大小或修改时间变化，最长约 30 秒自动检查并更新；同大小、同时间戳替换的文件请使用“重建词头缓存”。索引在后台生成，完成后整体替换。配置文件损坏时保留上一次成功的索引；单本词典失败时显示错误并载入其他正常词典。

| 字段 | 含义 |
| --- | --- |
| `uiLanguage` | 界面语言：`auto`（默认）、`en` 或 `zh-Hans` |
| `goldenDictPath` | GoldenDict-ng 的完整 exe 路径 |
| `defaultGroup` | 默认插件分组，必须与某个 `groups[].name` 一致 |
| `maxResults` | 候选数量上限，1–100；另可显示一条直接查询操作 |
| `debounceMilliseconds` | 输入防抖，默认 60 ms |
| `fuzzyMatching` | 是否补充拼写纠错候选 |
| `maxEditDistance` | 允许 0–2 处编辑；长度不超过 4 的输入最多 1 处，少于 3 字符不纠错 |
| `ignoreDiacritics` | 候选匹配是否忽略重音符号；保留原始词头显示和跳转 |
| `groups[].name` | Palette 中的分组名称 |
| `groups[].goldenDictGroup` | GoldenDict-ng 中已经存在的分组名称；null/省略则沿用其当前分组 |
| `dictionaries[].name` | 候选中显示的词典名称 |
| `dictionaries[].path` | MDX 路径，支持相对配置目录的路径及 `%变量%` |
| `dictionaries[].enabled` | 是否参与该组的候选检索 |

如果选择直接编辑底层文件，可以使用 JSON 注释和尾逗号。JSON 字符串中的 Windows 路径需要写成 `C:\\Dictionaries\\example.mdx`；这仅适用于手工编辑 JSON，Palette 路径框不需要这样转义。不要把 `.mdd`、CSS 或 JS 加到配置中。

MDX 必须已经在 GoldenDict-ng 中配置。插件不会自动修改它的词典和分组。候选范围由本插件配置决定，释义范围由 GoldenDict-ng 的目标分组及词典启用状态决定；两者应保持对应。

## 匹配与性能

候选按精确原文、归一化精确匹配、前缀补全、拼写纠错排列。同类候选按归一化词头排序；纠错先按编辑距离排序。为控制噪声，前缀结果足够时不额外计算纠错。支持插入、删除、替换和相邻字母颠倒，采用 Optimal String Alignment 编辑距离；不承诺与 GoldenDict-ng 内置候选完全一致。

词头以原始拼写去重，匹配键执行 Unicode 兼容归一化和大小写处理，可选重音折叠。编辑距离以 UTF-16 单元计算；补充平面字符的编辑成本可能大于一个字符。

前缀查询使用有序数组和二分查找；纠错遍历数组存储的 trie，并通过编辑距离剪枝。输入后立即清除旧候选，并通过取消令牌和查询序号避免旧查询覆盖新输入。

每本词典有独立的二进制词头缓存，记录格式版本、MDX 大小及时间戳，带 SHA-256 校验和，通过临时文件原子替换。运行时索引完全由本插件维护。缓存中仅保存词头，不复制释义。

本次真实词库验证：Oxford 615,913 词头，PONS 68,437 词头；合并去重后 638,025 词头。详细测试记录见 `VALIDATION.md`。这些是当前机器上的观测值，不是其他机器或词库的性能保证。

## 格式支持与边界

- MDX 1.x/2.x 的词头区，未加密和 `Encrypted=2` 元数据。
- 无压缩、zlib、LZO1X；文件头、词头区和解压块校验。
- UTF-8、UTF-16LE、GBK/GB18030、Big5，以及 .NET 支持的其他单字节编码。
- 暂不支持 MDX 3.x、密码/设备绑定词库。错误会标明对应词典。
- 单个压缩/解压块上限 128 MiB，文件头上限 4 MiB；单个候选词头最多 256 个 UTF-16 单元。
- GoldenDict-ng 的样式、脚本、图片及资源兼容由其自身处理。本扩展不作额外转换。

## 从源码构建

需要 .NET 10 SDK。依赖全部通过 NuGet 获取，Windows 打包工具也通过 NuGet 提供，无需完整 Visual Studio。构建脚本不会安装 SDK 或改变系统设置。

```powershell
.\scripts\Test.ps1
.\scripts\Build.ps1
.\scripts\Install.ps1
```

默认构建 `win-x64` 自包含版本。`Build.ps1 -Runtime win-arm64` 可构建 ARM64；`-FrameworkDependent` 可减小分发体积，但目标电脑需 .NET 10 运行时。ARM64 没有在 ARM64 硬件上验证。

调试命令行工具：

```powershell
dotnet run --project .\src\PaletteDictionary.Cli -- inspect "D:\Dictionaries\example.mdx"
dotnet run --project .\src\PaletteDictionary.Cli -- search .\config.example.json aple
dotnet run --project .\src\PaletteDictionary.Cli -- open .\config.example.json apple
```

预编译目录也包含 `PaletteDictionary.Cli.exe`，可用相同的 `inspect`、`search`、`open` 参数直接运行，无需 SDK。

额外的跨进程 COM 集成测试会启动本扩展、检查候选页面与原生设置表单，然后释放宿主。它使用临时配置副本，不会修改传入的配置，不会发送查词或修改 GoldenDict 配置：

```powershell
dotnet run --project .\tests\PaletteDictionary.Smoke -- .\artifacts\win-x64\PaletteDictionary.Extension.exe .\config.example.json
```

正式对外分发应另行构建并签名 MSIX 或发布到合适渠道。本次提供的是本地开发注册版本，不附带签名证书，也不要求导入证书。

## 卸载

```powershell
.\scripts\Uninstall.ps1
```

卸载保留配置和缓存，不影响 GoldenDict-ng 或原始词典。

## 许可证

GPL-3.0-or-later。LZO 适配代码原始许可为 GPL-2.0-or-later，RIPEMD-128 和 COM 模板包含 MIT 代码。详见 `LICENSE`、`THIRD-PARTY-NOTICES.md` 和 `licenses/`。不附带任何词典文件。

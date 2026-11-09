; ===========================================================================================
;  SteelStructureSuite（参数化钢结构套件）安装脚本（Inno Setup 6.x）
;    · 以 Autodesk Bundle 形式安装到 AutoCAD 插件目录，随 AutoCAD 启动自动加载
;    · 目标版本：AutoCAD 2019 (R23.0) ~ AutoCAD 2024 (R24.3)，Windows 10 x64
;
;  编译方式：
;    1) Inno Setup Compiler 打开本文件 → Build → Compile
;    2) 或命令行：iscc.exe /DAppVersion=1.0.0 SteelStructureSuite.iss
; ===========================================================================================

#define AppName "参数化钢结构套件"
#define AppShortName "SteelStructureSuite"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "zhangly1403"
#define AppCopyright "Copyright @ zhangly1403 2026 | zhangly1403@qq.com"

; 已编译产物目录：优先 Release，其次 Debug（可用 /DRuntimeDir=... 覆盖）
#ifndef RuntimeDir
  #if FileExists("..\SteelStructureSuite\bin\x64\Release\SteelStructureSuite.dll")
    #define RuntimeDir "..\SteelStructureSuite\bin\x64\Release"
  #else
    #define RuntimeDir "..\SteelStructureSuite\bin\x64\Debug"
  #endif
#endif

#define BundleName "SteelStructureSuite.bundle"
#define BundleDir "{commonpf}\Autodesk\ApplicationPlugins\" + BundleName

[Setup]
AppId={{62F3CFDA-6F06-467A-ADA0-9948368EA1C5}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright={#AppCopyright}
DefaultDirName={commonappdata}\SteelStructureSuite
UninstallFilesDir={commonappdata}\SteelStructureSuite
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=no
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=SteelStructureSuite-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
; Bundle 自动加载清单
Source: "PackageContents.xml"; DestDir: "{#BundleDir}"; Flags: ignoreversion

; 主程序（AutoCAD 各托管程序集由 AutoCAD 自身提供，无需打包）
Source: "{#RuntimeDir}\SteelStructureSuite.dll"; DestDir: "{#BundleDir}\Contents"; Flags: ignoreversion

; 使用说明与许可证（随 Bundle 一起分发，并提供独立副本）
Source: "..\docs\UserManual.md"; DestDir: "{#BundleDir}\Contents\Help"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{#BundleDir}\Contents\Help"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\docs\UserManual.md"; DestDir: "{app}"; DestName: "使用说明.md"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{group}\使用说明"; Filename: "{sys}\notepad.exe"; Parameters: """{app}\使用说明.md"""

[UninstallDelete]
; 清理 Bundle 目录下的空壳目录（文件本身由安装记录负责删除）
Type: dirifempty; Name: "{#BundleDir}\Contents\Help"
Type: dirifempty; Name: "{#BundleDir}\Contents"
Type: dirifempty; Name: "{#BundleDir}"

[Code]
procedure CleanLegacyBundles;
begin
  { 1. 用户级同名 Bundle 必须删除，否则 AutoCAD 会同时加载两份 DLL }
  DelTree(ExpandConstant('{userappdata}') + '\Autodesk\ApplicationPlugins\' + '{#BundleName}', True, True, True);

  { 2. 历史遗留包名（如曾用缩写或测试包名，可在此追加 DelTree 调用） }

  { 3. 安装目标 Contents 彻底重建，防止旧版本残留孤儿 DLL }
  DelTree(ExpandConstant('{#BundleDir}') + '\Contents', True, True, True);
end;

function InitializeSetup(): Boolean;
begin
  CleanLegacyBundles;
  Result := True;
end;

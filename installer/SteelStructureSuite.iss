; ===========================================================================================
;  SteelStructureSuite（参数化钢结构套件）安装脚本（Inno Setup 6.x）
;    · 以 Autodesk Bundle 统一自适应应用包形式安装到受 SECURELOAD=1 信任的系统插件目录：
;      C:\Program Files\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle\
;    · 目标版本：AutoCAD / Plant 3D 2019 ~ 2026+ 全系列自适应，Windows 10/11 x64
;
;  编译方式：
;    1) 先用 pwsh tools\Publish.ps1 生成统一 Bundle 与各版本编译产物
;    2) 命令行调用：iscc.exe /DAppVersion=1.0.0 SteelStructureSuite.iss
; ===========================================================================================

#define AppName "参数化钢结构套件"
#define AppShortName "SteelStructureSuite"
#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
#define AppPublisher "zhangly1403"
#define AppCopyright "Copyright (c) 2026 zhangly1403 | zhangly1403@qq.com"

#define RuntimeDirR23 "..\SteelStructureSuite\bin\x64\Release_R23"
#define RuntimeDirR24 "..\SteelStructureSuite\bin\x64\Release_R24"
#define RuntimeDirR25 "..\SteelStructureSuite\bin\x64\Release_R25"

#define BundleName "SteelStructureSuite.bundle"
#define BundleDir "{commonpf64}\Autodesk\ApplicationPlugins\" + BundleName

[Setup]
AppId={{62F3CFDA-6F06-467A-ADA0-9948368EA1C5}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright={#AppCopyright}
DefaultDirName={commonappdata}\SteelStructureSuite
UninstallFilesDir={commonappdata}\SteelStructureSuite
UsePreviousAppDir=no
DirExistsWarning=no
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
AlwaysShowDirOnReadyPage=no
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

[Dirs]
Name: "{#BundleDir}"; Permissions: users-readexec

[Files]
; 统一 Bundle 自动加载清单（内含 2019~2026 对应 Components 配置，根据 CAD 运行时版本自动匹配加载）
Source: "PackageContents.xml"; DestDir: "{#BundleDir}"; Flags: ignoreversion

; 2019~2020 模块 (.NET 4.8)
Source: "{#RuntimeDirR23}\SteelStructureSuite.dll"; DestDir: "{#BundleDir}\Contents\2019"; Flags: ignoreversion
Source: "{#RuntimeDirR23}\SteelStructureSuite.pdb"; DestDir: "{#BundleDir}\Contents\2019"; Flags: ignoreversion skipifsourcedoesntexist

; 2021~2024 模块 (.NET 4.8)
Source: "{#RuntimeDirR24}\SteelStructureSuite.dll"; DestDir: "{#BundleDir}\Contents\2021"; Flags: ignoreversion
Source: "{#RuntimeDirR24}\SteelStructureSuite.pdb"; DestDir: "{#BundleDir}\Contents\2021"; Flags: ignoreversion skipifsourcedoesntexist

; 2025~2026 模块 (.NET 8.0)
Source: "{#RuntimeDirR25}\SteelStructureSuite.dll"; DestDir: "{#BundleDir}\Contents\2026"; Flags: ignoreversion
Source: "{#RuntimeDirR25}\SteelStructureSuite.pdb"; DestDir: "{#BundleDir}\Contents\2026"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#RuntimeDirR25}\SteelStructureSuite.deps.json"; DestDir: "{#BundleDir}\Contents\2026"; Flags: ignoreversion skipifsourcedoesntexist

; 默认截面库数据文件（首次启动时加载并生成用户本地 SectionCatalog_MultiTable.csv）
Source: "..\SteelStructureSuite\Data\DefaultSectionCatalog.csv"; DestDir: "{#BundleDir}\Contents\Data"; Flags: ignoreversion

; 使用说明与许可证
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
Type: dirifempty; Name: "{#BundleDir}\Contents\Data"
Type: dirifempty; Name: "{#BundleDir}\Contents\Help"
Type: dirifempty; Name: "{#BundleDir}\Contents\2019"
Type: dirifempty; Name: "{#BundleDir}\Contents\2021"
Type: dirifempty; Name: "{#BundleDir}\Contents\2026"
Type: dirifempty; Name: "{#BundleDir}\Contents"
Type: dirifempty; Name: "{#BundleDir}"

[Code]
procedure CleanStaleAutoCadRegistryCache;
var
  Releases, Products, AppKeys, LoadedKeys: TArrayOfString;
  I, J, K: Integer;
  AcadRoot, RelPath, ProdPath, AppsPath, LoadedPath, KeyName: String;
begin
  AcadRoot := 'Software\Autodesk\AutoCAD';
  if not RegGetSubkeyNames(HKCU, AcadRoot, Releases) then Exit;

  for I := 0 to GetArrayLength(Releases) - 1 do
  begin
    RelPath := AcadRoot + '\' + Releases[I];
    if RegGetSubkeyNames(HKCU, RelPath, Products) then
    begin
      for J := 0 to GetArrayLength(Products) - 1 do
      begin
        ProdPath := RelPath + '\' + Products[J];

        AppsPath := ProdPath + '\Applications';
        if RegGetSubkeyNames(HKCU, AppsPath, AppKeys) then
        begin
          for K := 0 to GetArrayLength(AppKeys) - 1 do
          begin
            KeyName := Uppercase(AppKeys[K]);
            if Pos('STEELSTRUCTURESUITE', KeyName) > 0 then
              RegDeleteKeyIncludingSubkeys(HKCU, AppsPath + '\' + AppKeys[K]);
          end;
        end;

        LoadedPath := ProdPath + '\Loaded';
        if RegGetSubkeyNames(HKCU, LoadedPath, LoadedKeys) then
        begin
          for K := 0 to GetArrayLength(LoadedKeys) - 1 do
          begin
            KeyName := Uppercase(LoadedKeys[K]);
            if Pos('STEELSTRUCTURESUITE', KeyName) > 0 then
              RegDeleteKeyIncludingSubkeys(HKCU, LoadedPath + '\' + LoadedKeys[K]);
          end;
        end;
      end;
    end;
  end;
end;

procedure CleanLegacyBundles;
begin
  { 1. 清理 ProgramData / AppData / Program Files (x86) 及历史带版本号 Bundle，杜绝多副本冲突 }
  DelTree(ExpandConstant('{commonappdata}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle', True, True, True);
  DelTree(ExpandConstant('{commonappdata}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite2021.bundle', True, True, True);
  DelTree(ExpandConstant('{commonappdata}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite2026.bundle', True, True, True);

  DelTree(ExpandConstant('{userappdata}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle', True, True, True);
  DelTree(ExpandConstant('{userappdata}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite2021.bundle', True, True, True);
  DelTree(ExpandConstant('{userappdata}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite2026.bundle', True, True, True);

  DelTree(ExpandConstant('{commonpf32}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite.bundle', True, True, True);
  DelTree(ExpandConstant('{commonpf32}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite2021.bundle', True, True, True);
  DelTree(ExpandConstant('{commonpf32}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite2026.bundle', True, True, True);

  DelTree(ExpandConstant('{commonpf64}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite2021.bundle', True, True, True);
  DelTree(ExpandConstant('{commonpf64}') + '\Autodesk\ApplicationPlugins\SteelStructureSuite2026.bundle', True, True, True);

  { 2. 安装目标 Contents 彻底重建，防止旧版本残留孤儿 DLL }
  DelTree(ExpandConstant('{#BundleDir}') + '\Contents', True, True, True);
end;

function InitializeSetup(): Boolean;
begin
  CleanLegacyBundles;
  CleanStaleAutoCadRegistryCache;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    CleanStaleAutoCadRegistryCache;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { 强制跳过安装位置选择页面，位置锁定为受信任的系统插件目录，不可选不可变 }
  if PageID = wpSelectDir then
    Result := True
  else
    Result := False;
end;

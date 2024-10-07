[Setup]
AppName=Kairo
AppVerName=Kairo - 1.2
DefaultDirName={pf}\Kairo
DefaultGroupName=Kairo
UninstallDisplayIcon={app}\Kairo.exe
Compression=lzma2
SolidCompression=yes
OutputDir=Output
AppPublisher=����ӣܿ����Ƽ����޹�˾
AppPublisherURL=https://www.Kairo.cn
PrivilegesRequired=admin

[Files]
Source: "D:\Frp\Kairo-Impl\Kairo\bin\Release\Kairo.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Frp\Kairo-Impl\Kairo\bin\Release\Microsoft.Exchange.WebServices.Auth.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Frp\Kairo-Impl\Kairo\bin\Release\Microsoft.Exchange.WebServices.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Frp\Kairo-Impl\Kairo\bin\Release\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Frp\Kairo-Impl\Kairo\bin\Release\frpc.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Frp\Kairo-Impl\Kairo\bin\Release\HandyControl.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "D:\Frp\Kairo-Impl\Kairo\resource\*"; DestDir: "{app}\resource"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Kairo"; Filename: "{app}\Kairo.exe"; IconFilename: "{app}\resource\favicon.ico"
; Add other shortcuts you want to create here

[Icons]
Name: "{commondesktop}\Kairo"; Filename: "{app}\Kairo.exe"; IconFilename: "{app}\resource\favicon.ico";


[Run]
Filename: "{app}\Kairo.exe"; Description: "ֱ������ Kairo"; Flags: postinstall nowait runascurrentuser

[Registry]
Root: HKCR; Subkey: "Kairo"; ValueType: string; ValueName: ""; ValueData: "Kairo Desktop Application Custom URL Scheme."
Root: HKCR; Subkey: "Kairo"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCR; Subkey: "Kairo\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\resource\favicon.ico"
Root: HKCR; Subkey: "Kairo\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Kairo.exe"" ""%1"""
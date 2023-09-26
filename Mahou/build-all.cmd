@ECHO OFF
call "%~dp0\build.cmd" release x86
call "%~dp0\build.cmd" release x64
call "%~dp0\build.cmd" release x86_x64
call "%~dp0\build.cmd" debug x86
call "%~dp0\build.cmd" debug x64
call "%~dp0\build.cmd" debug x86_x64
rmdir /Q /S "%~dp0\..\BUILD"
mkdir "%~dp0\..\BUILD"
cd "%~dp0\..\JKL\bin\"
::make re zip
7z a "%~dp0\..\BUILD\jkl.zip" -mx=9 "*"
7z a "%~dp0\..\BUILD\Release_x86_x64.zip" -mx=9 "*"
cd "%~dp0\bin\Release_x86_x64"
7z a "%~dp0\..\BUILD\Release_x86_x64.zip" -mx=9 "Mahou.exe"
cd "%~dp0\bin\Release_x86\"
7z a "%~dp0\..\BUILD\Release_x86.zip" -mx=9 "Mahou.exe"
cd "%~dp0\bin\Release_x64\"
7z a "%~dp0\..\BUILD\Release_x64.zip" -mx=9 "Mahou.exe"
cd "%~dp0\bin\Debug_x86_x64\"
7z a "%~dp0\..\BUILD\Debug_x86_x64.zip" -mx=9 "Mahou.exe"
cd "%~dp0\bin\Debug_x86\"
7z a "%~dp0\..\BUILD\Debug_x86.zip" -mx=9 "Mahou.exe"
cd "%~dp0\bin\Debug_x64\"
7z a "%~dp0\..\BUILD\Debug_x64.zip" -mx=9 "Mahou.exe"
cd "%~dp0\.."
7z a "%~dp0\..\BUILD\AS_dict.zip" -mx=9 "AS_dict.txt"
cd "%~dp0"
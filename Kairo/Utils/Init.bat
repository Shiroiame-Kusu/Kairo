@echo off
chcp 65001

cd ../Utils
echo.local>buildinfo.info & echo.%date% %time%>>buildinfo.info & echo.null>>buildinfo.info
exit 0

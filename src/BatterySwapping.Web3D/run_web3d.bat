@echo off
title Web 3D Digital Twin Simulator - Tram Doi Pin BSS
echo ========================================================
echo   KHOI DONG WEB 3D DIGITAL TWIN SIMULATOR (PORT 5000)
echo ========================================================
python run_web3d.py
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Khong tim thay Python hoac co loi xay ra.
    echo Vui long mo truc tiep file index.html bang trinh duyet web!
    pause
)

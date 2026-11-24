#!/usr/bin/env bash
# Web 3D Digital Twin Simulator Launcher for Ubuntu / Linux
echo "========================================================"
echo "  KHOI DONG WEB 3D DIGITAL TWIN SIMULATOR (PORT 5000)  "
echo "========================================================"
if command -v python3 &>/dev/null; then
    python3 run_web3d.py
elif command -v python &>/dev/null; then
    python run_web3d.py
else
    echo "Khong tim thay Python! Vui long mo file index.html bang trinh duyet:"
    echo "xdg-open index.html"
    xdg-open index.html 2>/dev/null || sensible-browser index.html 2>/dev/null
fi

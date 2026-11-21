#!/usr/bin/env python3
"""
Launcher cho Web 3D Digital Twin Simulator (Hệ thống Trạm Đổi Pin BSS)
Đa luồng (ThreadingTCPServer) - Tương thích 100% trên cả Windows và Ubuntu Linux.
Không yêu cầu cài đặt thêm node_modules, npm hay bất kỳ thư viện bên ngoài nào.
"""

import os
import sys
import webbrowser
import http.server
import socketserver
import threading
import time

PORT = 5000
DIRECTORY = os.path.dirname(os.path.abspath(__file__))

class QuietHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIRECTORY, **kwargs)
        
    def log_message(self, format, *args):
        # Không in spam log console
        pass

class ThreadedHTTPServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    allow_reuse_address = True
    daemon_threads = True

def open_browser():
    time.sleep(1.0)
    url = f"http://localhost:{PORT}"
    print(f"\n=======================================================")
    print(f"🚀 Web 3D Digital Twin Simulator đang chạy!")
    print(f"🌐 Truy cập: {url}")
    print(f"📌 Nhấn Ctrl+C để dừng máy chủ.")
    print(f"=======================================================\n")
    try:
        webbrowser.open(url)
    except Exception:
        pass

def run():
    os.chdir(DIRECTORY)
    try:
        httpd = ThreadedHTTPServer(("", PORT), QuietHandler)
        threading.Thread(target=open_browser, daemon=True).start()
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n[Web3D Server] Đã dừng máy chủ thành công.")
    except Exception as e:
        print(f"\n[Lỗi] Không thể mở cổng {PORT}: {e}")

if __name__ == "__main__":
    run()

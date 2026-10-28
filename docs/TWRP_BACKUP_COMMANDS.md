# Lệnh TWRP native

Chi tiết workflow, cache và Stop: [TWRP_BACKUP.md](TWRP_BACKUP.md).

```text
adb -s <serial> get-state
```

Chỉ output `recovery` cho phép bắt đầu native. Chuyển recovery chỉ khi cần, dùng cấu hình đã chụp trước workflow:

```text
adb -s <serial> reboot recovery                    # starlte
adb -s <serial> reboot bootloader                  # sargo / tissot
fastboot -s <serial> boot <selected-recovery-image> # sargo / tissot
```

Không query lại codename/serial, không kiểm tra boot ID, mount, decrypt, storage hoặc khả năng phân vùng.

```text
adb -s <serial> shell twrp backup D <name>
adb -s <serial> pull /data/media/0/TWRP/BACKUPS/<serial>/<name> <operation-cache>/payload
```

Backup phải có terminal output, exit 0 và payload hợp lệ. Chỉ sau Download → ZIP tạm → Verify mới công bố ZIP chính thức và cleanup.

```text
adb -s <serial> push <extracted-file> /data/media/0/TWRP/BACKUPS/<serial>/restore_<operation-id>/<file>
adb -s <serial> shell twrp restore /data/media/0/TWRP/BACKUPS/<serial>/restore_<operation-id> D
```

Upload/cleanup chỉ dùng thư mục operation sở hữu. File ownership và interlock tạm trong `/tmp` bảo vệ chống chạy chồng; không chứa nhận diện recovery và không cho phép resume.

Process ADB native dùng streaming stdout/stderr và `WaitForExitAsync`; silence không phải lỗi. Stop hủy công việc trên PC. Khi native chưa chắc đã dừng, giữ dữ liệu remote/interlock, chỉ dọn dữ liệu tạm trên PC và giữ log. Không replay command hoặc tự retry restore.

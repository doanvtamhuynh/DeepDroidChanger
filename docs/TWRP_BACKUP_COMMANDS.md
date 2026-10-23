# Backup bằng TWRP CLI — luồng hiện tại

Backup chỉ chọn Data; Restore hỗ trợ ZIP phiên bản 3/4/5. Mã nguồn đối chiếu: `TwrpRecoveryService.CliBackup.cs`, `TwrpScripts.cs`, `Assets/TWRP/guard.sh`.

Ví dụ Name `abc.zip`, Folder `<exe>/Backup`, serial `SERIAL`. Tên thực tế luôn lấy từ ô Name rồi bỏ đuôi `.zip`.

1. Tạo `<exe>/Backup/cache/abc`, gắn operation ID để không nhận nhầm cache cũ. Từ chối ZIP/cache trùng tên trước khi ghi boot.
2. Khi Android online, dùng root lease, đọc slot/block hiện tại và lưu boot:

```sh
dd if=/dev/block/by-name/boot_<slot> of=<staging>/deepdroid-twrp/<OP>/original.img bs=4194304 conv=fsync
```

```powershell
adb -s SERIAL pull <staging>/deepdroid-twrp/<OP>/original.img <exe>/Backup/cache/abc/original.img
```

App giữ boot/journal/script/log ngay trong `Backup/cache/abc`; ảnh boot tạm vẫn có guard chạy độc lập trong recovery init. Ghi boot theo slot đã xác định, không đổi slot hoặc ghi vbmeta/misc:

```sh
dd if=<staging>/deepdroid-twrp/<OP>/recovery.img of=/dev/block/by-name/boot_<slot> bs=4194304 conv=fsync
```

```powershell
adb -s SERIAL reboot recovery
```

3. Guard giữ original.img trong RAM, kiểm tra Data đã đọc được và cấu hình storage/folder đích. Lệnh backup thực tế là:

```sh
twrp backup D abc
```

Tương đương khi chạy tay từ PC trong recovery:

```powershell
adb -s SERIAL shell twrp backup D abc
```

`D` chỉ chọn Data; không có O/M nên dùng mặc định CLI là không nén native và có digest. [Nguồn TeamWin](https://github.com/TeamWin/android_bootable_recovery/blob/android-12.1/openrecoveryscript.cpp). Guard từ chối hàng đợi `/tmp/openrecoveryscript` còn nội dung; không chạy thêm lệnh tay song song workflow app.

4. Kiểm tra đúng `/data/media/0/TWRP/BACKUPS/SERIAL/abc`: thư mục mới, recovery.log đã được TWRP tạo ở cuối backup, payload Data không rỗng. Chỉ có dòng Backup Complete hoặc thư mục tồn tại là chưa đủ. App liệt kê file/kích thước rồi kéo từng file nguyên tên để kiểm tra và hỗ trợ retry:

```powershell
adb -s SERIAL pull /data/media/0/TWRP/BACKUPS/SERIAL/abc/<file> <exe>/Backup/cache/abc/payload/abc/<file>.partial
```

Tệp tải đủ mới đổi từ `.partial` sang tên native. Bước này diễn ra trong recovery, trước khi trả boot. Kéo cả thư mục bằng tay có cấu trúc tương đương:

```powershell
adb -s SERIAL pull /data/media/0/TWRP/BACKUPS/SERIAL/abc <exe>/Backup/cache/abc/
```

5. Khi PC đã kéo đủ, guard nhận xác nhận đúng operation ID, hoàn nguyên boot và reboot Android:

```sh
dd if=<WORK>/original.img of=/dev/block/by-name/boot_<slot> bs=4194304 conv=fsync
sync
reboot system
```

Không reboot khi hoàn nguyên thất bại hoặc chưa xác nhận engine đã dừng. Stop vẫn có confirm và không ngắt đoạn ghi boot.

6. Khi Android online, tạo ZIP64 qua `.partial`, kiểm chứng rồi đổi tên cuối `<Folder>/abc.zip`:

```text
backup.json        (manifest phiên bản 5, tích hợp metadata thiết bị; không có type.json hoặc boot cứu hộ)
abc/
  data.*.win*
  ...digest/metadata native...
  recovery.log
```

Sau khi ZIP đã công bố và kiểm chứng, xóa đúng thư mục native có marker `.deepdroid-operation` khớp OP; chỉ khi bước này thành công mới xóa toàn bộ cache/abc có marker OP, gồm boot/journal/script/log. Không ghi journal sau khi cache đã xóa. Không xóa thư mục serial/backup khác. Root lease cleanup trên Android trả quyền shell như cơ chế hiện có. Cache chỉ được giữ khi lỗi/Stop hoặc cleanup thất bại; ZIP đầu ra được giữ nguyên.

Lỗi pull/nén/cleanup giữ phần dữ liệu còn lại để retry xuất ZIP hoặc cleanup, không chạy lại backup/flash. Luồng Restore hiện nhận ZIP định dạng 3/4/5; các API cũ vẫn đọc được định dạng 1/2.

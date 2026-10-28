# TWRP Backup / Restore

Ứng dụng chạy native TWRP qua process ADB trên Windows. Trạng thái thao tác chỉ tồn tại trong bộ nhớ. Không có Guard, Record, RAM runner, sửa/flash boot, entry receipt, journal hay Check/Resume/Recover/Retry Export.

## Context và recovery

Serial được chụp từ thiết bị ở thời điểm mở dialog và đưa vào request bất biến. Tất cả lệnh ADB/Fastboot dùng `-s` với serial đó. Kiểm tra cú pháp argument và an toàn đường dẫn vẫn được giữ; không query hoặc đối chiếu lại nhận diện thiết bị trong workflow.

Preflight thiết bị duy nhất là `adb -s <serial> get-state`, yêu cầu output `recovery`. Nếu đã ở recovery, không reboot recovery. Nếu chưa, dùng cấu hình đã chọn trước khi chạy:

- `starlte`: `adb -s <serial> reboot recovery`, dùng recovery đã cài.
- `sargo`: reboot bootloader rồi `fastboot -s <serial> boot <twrp_sargo.img>`.
- `tissot`: tương tự, dùng `twrp_tissot_treble.img`.

Ảnh nguồn được kiểm tra SHA-256 cục bộ và giữ handle đọc trong lúc fastboot chạy. Không dựng ảnh nhúng script và không flash phân vùng. Sau khi vào recovery, chỉ poll `get-state` đến deadline. ADB không phản hồi hoặc không vào được recovery sẽ trả lỗi.

Dialog có thể đọc marker một lần để điền cấu hình image trước khi submit. Workflow không gọi marker, codename, model, fingerprint, boot ID, mount, decrypt, storage space, partition capability hoặc Data size. Khi mở dialog trong recovery mà không biết marker, backup vẫn chạy với metadata `recovery`; chỉ cần thông tin image nếu phải chuyển chế độ.

## Backup

1. Chụp serial/context, kiểm tra tên ZIP/đường dẫn trên PC.
2. Kiểm tra recovery, vào recovery nếu cần.
3. Giữ quyền thực thi độc quyền và giữ chỗ đường dẫn payload chưa tồn tại trong khóa ownership tạm. Để native tự tạo thư mục backup.
4. Chạy đúng một lần `adb -s <serial> shell twrp backup D <name>`.
5. Stream stdout/stderr, theo dõi PID, elapsed, trạng thái process, timeout cấu hình và phần trăm nếu native có trả về.
6. Chờ process thoát. Yêu cầu exit code 0, completion native đúng loại thao tác và không có lỗi native được nhận diện. Exit 0 hoặc output im lặng không đủ chứng minh thành công.
7. Pull `/data/media/0/TWRP/BACKUPS/<serial>/<name>` về `payload/<name>` trong cache.
8. Kiểm tra tên file, file thường, split liên tục, file Data không rỗng, recovery.log; đối chiếu inventory và kích thước file remote sau completion với file tải về.
9. Ghi ZIP tạm ở thư mục đích, tính SHA-256 từng payload và ghi vào `backup.json` trong ZIP. Đọc lại ZIP, xác minh manifest, kích thước và SHA-256 trước khi đổi sang tên ZIP chính thức. Không overwrite ZIP cũ.
10. Dọn payload remote đã xác nhận ownership, giải phóng interlock, reboot Android, xóa cache thao tác rồi báo Completed.

Backup tạo cache sau khi xác nhận recovery. Log chuẩn bị/vào recovery được giữ trong bộ nhớ và flush khi cache được mở; nếu vào recovery lỗi thì tạo attempt chỉ giữ log. Restore tạo cache mới để xác minh/extract ZIP trước recovery. Không dùng cache cũ để quyết định bước workflow. Trùng tên cache thì tạo `<name>_attempt_<operation-id>`, giữ nguyên log cũ.

## Restore

1. Chụp serial/context và xác minh/extract ZIP vào cache mới.
2. Kiểm tra định dạng, đường dẫn, split và độ dài; xác minh SHA-256 nếu manifest có cung cấp. Không đối chiếu serial/codename trong ZIP với thiết bị đang chọn.
3. Kiểm tra/vào recovery theo cấu hình đã chọn.
4. Upload vào `/data/media/0/TWRP/BACKUPS/<serial>/restore_<operation-id>` có ownership riêng.
5. Chạy đúng một lần `adb -s <serial> shell twrp restore <path> D` và stream process như Backup.
6. Chỉ cleanup remote/reboot khi native có terminal result đáng tin cậy. Restore lỗi/Stop không tự retry hoặc replay.

Giữ reader của ZIP format 1–6. Restore Data tiếp tục nhận ZIP Data format 3–6, kể cả format cũ có `original.img`/`type.json`; không thực thi nội dung boot từ archive. SHA-256 mới là trường tùy chọn nên ZIP cũ hợp lệ vẫn đọc được.

## Stop, lỗi và chống chạy chồng

Coordinator chặn hai workflow đồng thời trên một serial. Ngay trước native, ứng dụng dùng interlock `/tmp/deepdroid-native-active` và kiểm tra process CLI native đang tồn tại. Đây là bảo vệ ownership/không chạy chồng, không phải preflight Data/Storage hoặc session có thể resume. Interlock được tạo nguyên tử và chỉ giải phóng bởi operation sở hữu nó.

Stop đánh dấu Stopping, hủy PC process/transfer/ZIP và chờ đóng stream/handle trước cleanup. Không dùng `adb kill-server`, kill recovery, wipe, format hoặc reboot cưỡng bức.

Kill ADB, disconnect hoặc timeout không chứng minh native đã dừng. Khi thiếu terminal evidence:

- báo kết quả native chưa xác định, giữ log;
- dọn payload và ZIP tạm trên PC;
- giữ nguyên toàn bộ payload remote và interlock vì native có thể đang ghi;
- chặn lệnh native mới, không tự reconnect/resume hoặc replay.

Native chưa được xác nhận kết thúc nên ownership remote vẫn còn hiệu lực. Sau khi kiểm tra thiết bị đã ngừng ghi, người dùng cần chủ động khởi động lại recovery và ứng dụng để bỏ trạng thái không xác định; ứng dụng không tự reboot hoặc tự gỡ khóa. Sau crash, interlock ở recovery vẫn chặn chạy chồng. Các payload PC còn sót do crash không được tự nhận lại hay tiếp tục tải/ZIP.

Nếu native đã có terminal result, chỉ xóa đúng đường dẫn được giữ chỗ khi chưa tồn tại, dưới interlock có owner khớp operation hiện tại và canonical path đúng dự kiến. Restore có thêm marker `.deepdroid-operation` bên trong thư mục upload. Không xóa backup khác. Marker ownership không dùng để nhận diện recovery hoặc resume.

Không tạo trước thư mục backup vì [CLI TeamWin kiểm tra tên đã tồn tại](https://github.com/TeamWin/Team-Win-Recovery-Project/blob/android-9.0/openrecoveryscript.cpp#L151-L180), rồi [từ chối thư mục đích có sẵn](https://github.com/TeamWin/Team-Win-Recovery-Project/blob/android-9.0/partitionmanager.cpp#L577-L585). Khóa `/tmp` chỉ giữ operation ID và đường dẫn phục vụ cleanup.

## Cache và log

Khi chạy:

```text
Backup/cache/<name>/
  payload/<native-name>/...
  logs/process.log
  logs/recovery.log
  logs/operation.log
```

Sau lỗi/Stop: chỉ giữ `logs/` và ba log trên. Xóa payload, JSON metadata extract, marker PC, download dở và ZIP tạm thuộc operation. File bị khóa hoặc cleanup lỗi được ghi rõ vào log/UI; không báo cleanup thành công giả. Nếu không ghi được operation.log, logger ứng dụng ghi lỗi thay thế và workflow vẫn thử cleanup.

Sau thành công: cache thao tác được xóa; ZIP chính thức ở thư mục người dùng chọn. Log các attempt thất bại cũ được giữ nguyên.

`process.log` ghi lệnh native, PID, mốc bắt đầu/kết thúc, stdout/stderr và exit code ngay khi nhận. `recovery.log` ghi native output và thu thêm `/tmp/recovery.log` khi có lỗi. `operation.log` ghi giai đoạn, verify, lỗi/Stop/timeout và kết quả cleanup. Không tạo `Backup/TWRP/`, `entry-<serial>.json`, `entry.json` hoặc `session.json`.

## Xác minh và giới hạn

Unit test dùng fake ADB và process runner, không cần điện thoại. Test process streaming dùng process Windows cục bộ. Chạy `dotnet restore`, `dotnet build`, `dotnet test`; nếu test host bị giới hạn named pipe của sandbox, có thể chạy trực tiếp test executable.

Không có cơ chế hủy native đáng tin cậy trên mọi TWRP. Vì vậy kết quả không rõ ràng phải chặn chạy lại. Completion tiếng Anh được parse trực tiếp; completion bản dịch lấy từ XML ngôn ngữ trong recovery, không chạy thêm CLI trước native. Bản TWRP không có terminal output có thể bị từ chối dù lệnh đã trả 0. Đây là hành vi an toàn, cần xác minh trên recovery/image thực tế. SHA-256 của ZIP mới bảo vệ lần restore tiếp theo; ZIP cũ không có digest chỉ giữ các kiểm tra format/kích thước tương thích trước đây.

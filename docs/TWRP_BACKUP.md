# TWRP backup/restore — Pixel 3a (sargo)

## Phạm vi

Single Device, Pixel 3a `sargo`, ADB root; nhận diện duy nhất bằng `ro.deepdroid.device=sargo`. Samsung starlte là hướng mở rộng sau này, chưa được hỗ trợ bởi ảnh/nhận diện hiện tại. Không thêm batch hoặc nhập `.ddcbak`. Giai đoạn 1 và lỗi test có sẵn được giữ nguyên.

Ảnh nguồn duy nhất: `DeepDroidChanger/Assets/TWRP/twrp_sargo.img`, sao chép nguyên từ `D:\Rom\TWRP\twrp_sargo.img`, SHA-256:

```text
82133ef23effdc4787bc1f08a7854c96638b5d074421c226c02602630f231665
```

## Backup hiện tại — Data qua CLI

Cửa sổ Backup có nhóm **Config** gồm Name, Folder và TWRP ngay dưới Folder. Không có chọn partition trong Backup. Header, chọn ảnh, confirm, log và Stop giữ cơ chế hiện có.

Ví dụ tên `abc.zip`, Folder là `<exe>/Backup`:

```text
<exe>/Backup/cache/abc/ được tạo và gắn operation ID
 → root/slot/path hiện tại; lưu boot gốc và kéo về cache/abc/original.img
 → giữ journal, log và script cứu hộ ngay trong Backup/cache/abc
 → ghi ảnh TWRP có guard vào boot hiện tại
 → reboot recovery
 → guard kiểm tra Data và chạy twrp backup D abc
 → xác nhận thư mục /data/media/0/TWRP/BACKUPS/<serial>/abc mới, payload Data và recovery.log hoàn tất
 → kéo payload vào Backup/cache/abc/payload/abc/ khi còn trong recovery
 → PC gửi xác nhận đã kéo đủ
 → guard hoàn nguyên boot gốc, reboot Android
 → tạo Backup/abc.zip qua .partial, kiểm chứng rồi công bố
 → dọn đúng thư mục native trên điện thoại của operation ID
 → xóa toàn bộ cache/abc, gồm journal/boot/script/log; không lưu lại journal sau bước xóa
```

Tên dùng từ ô Name sau khi bỏ đuôi `.zip`, không hardcode `abc`/`adb`. Tên native là một token tối đa 64 ký tự gồm chữ ASCII, số, dấu chấm, gạch dưới/gạch ngang; từ chối khoảng trắng và `..`. Folder trên PC hỗ trợ Unicode. Trùng ZIP/cache được báo lỗi và giữ dữ liệu cũ.

Lệnh `D` chỉ chọn Data, không chọn Super/Persist/Boot, không nén native và có digest theo mặc định CLI TWRP. ZIP trên PC vẫn được nén. Data không chứa `/data/media`.

Guard từ chối hàng đợi `/tmp/openrecoveryscript` còn nội dung trước khi gọi CLI, từ chối thư mục đích đã có và không coi exit 0/thư mục đơn thuần là thành công. TWRP tạo `recovery.log` trong thư mục backup ở cuối `Run_Backup`; guard yêu cầu file này, payload Data có kích thước dương và host đối chiếu đủ các file đã kéo trước khi xác nhận hoàn nguyên.

ZIP mới dùng định dạng 5, chỉ gồm `backup.json` và `abc/<payload native>`; boot, script, journal và log vận hành không được đưa vào ZIP; không tạo `type.json` riêng. Manifest giữ `version`, `backupId`, `name`, `createdAt`, `deviceMarker` từ `ro.deepdroid.device`, `serial`, `slot`, `layoutHash`, `recoveryDevice`, `partitions` và `payload` (đường dẫn, kích thước, phân vùng). Không lưu inventory vận hành, các property rỗng hoặc tùy chọn cố định của luồng Data. ZIP định dạng 3 cũ vẫn phải có `type.json` khớp manifest; không tự chuyển đổi hoặc xóa archive cũ.

Lỗi pull/nén giữ cache, backup native và bản cứu hộ. Retry chỉ tải/xuất/dọn phần còn lại, không flash hoặc chạy lại backup; mỗi bước cleanup giới hạn đường dẫn và kiểm tra operation ID. Không xóa cache có chủ khác. Nếu ZIP đã công bố nhưng app bị đóng trước khi lưu journal/dọn xong, retry kiểm chứng chính ZIP đó trước khi dọn tiếp.

Guard giữ boot gốc trong RAM, xử lý Stop và hoàn nguyên độc lập với vòng đời dialog/PC. Engine còn chạy hoặc hoàn nguyên lỗi thì giữ NeedsRecovery. Khi engine xong nhưng host chưa xác nhận pull, guard chờ tối đa hai giờ rồi hoàn nguyên với lỗi và giữ backup native. Không tuyên bố xử lý được mất nguồn hoặc recovery không khởi động.

Phiên mới lưu `.operation`, `session.json`, `original.img`, `restore-boot.ps1`, `flash.sh`, log và metadata tại `Backup/cache/<tên backup>/`. Payload tạm nằm trong `payload/<tên backup>/`. Backup thành công chỉ được dọn toàn bộ cache sau khi ZIP đã kiểm chứng, boot đã hoàn nguyên và cleanup native thành công; lỗi/Stop giữ cache để thử lại. Không ghi journal sau khi cache đã xóa. Restore tiếp tục giữ journal/cứu hộ sau khi dọn payload. Không tạo runtime/cứu hộ riêng dưới `Backup/TWRP`; các journal cũ tại đó vẫn đọc được. Các kiểm tra nhận diện/root/slot/ảnh sargo và phạm vi thiết bị giữ nguyên; không mở workflow starlte/tissot trong lượt này.

## Restore từ ZIP trên PC

Cửa sổ Restore có nhóm Config: chọn ZIP, thông tin backup và ảnh TWRP. Không hiện cache hoặc dòng giải thích dưới Config. Hộp chọn ZIP tự tạo và mở tại `<exe>/Backup`. Luồng nhận ZIP định dạng 5 chỉ chứa metadata/payload hoặc định dạng 3/4 cũ có boot lịch sử, khôi phục Data theo nội dung backup; không có checkbox phân vùng/digest. Log và Stop tiếp tục nằm ở Single Device, confirm dùng dialog chung.

1. Tự tạo cache Restore tại `<exe>/Backup/cache/<tên ZIP không đuôi>_restore_<operation-id>/` để không ghi đè journal/cứu hộ Backup, gắn operation ID và giải nén lớp ZIP. Backup dùng `<exe>/Backup/cache/<tên ZIP không đuôi>/`, kể cả khi ZIP nằm ở thư mục khác; phiên dở dang cũ giữ cache đã ghi trong journal. Giữ nguyên byte các file `.win*`, không bung payload native. Từ chối entry trùng, symlink, đường dẫn vượt cache, payload thiếu/0 byte hoặc chuỗi chunk không đầy đủ trước khi flash.
2. Đọc metadata từ `backup.json`; so marker, serial, ảnh recovery, slot và layout với thiết bị. Định dạng 3 được đối chiếu thêm `type.json` khi đọc ZIP, không cần giải nén file này. Kiểm root/slot/layout vận hành theo preflight hiện có; không thêm property nhận diện. Sai metadata/thiết bị thì báo lỗi, dọn đúng cache mới tạo và không flash/reboot.
3. Upload thư mục native dưới tên trong manifest vào `/data/media/0/TWRP/BACKUPS/<serial>/<tên>` (cũng thấy qua `/sdcard/TWRP/BACKUPS/...`). Kiểm alias bằng marker operation ID, chặn thư mục đã có. Không upload `original.img` trong ZIP vào thư mục native.
4. Lưu boot hiện tại một lần thành `original.img` cạnh journal/script trong cache của phiên Restore. Boot lịch sử từ ZIP cũ chỉ được giải nén dưới `payload/`. Boot lịch sử trong ZIP không được dùng để hoàn nguyên. Dựng ảnh guard, ghi `FLASH_INTENT`, flash TWRP rồi reboot recovery.
5. Guard chạy native CLI `twrp restore <tên>`, không truyền bộ chọn khác. Tên lấy từ manifest nên đổi tên ZIP trên PC không làm đổi tên native. Cấu hình nơi lưu của TWRP trước khi chạy. Trong cùng yêu cầu ORS, thêm continuation ghi kết quả theo operation ID chỉ khi native restore thành công; không coi CLI exit 0 là đủ. Hoàn nguyên boot vẫn nằm trong guard độc lập.
6. Guard trả boot hiện tại, reboot Android khi restore thành công. Sau khi Android sẵn sàng, xóa đúng payload điện thoại có marker của phiên rồi chỉ dọn payload tạm/ảnh recovery; giữ ZIP nguồn, journal, log, script và boot cứu hộ trong cache.

Lỗi/Stop sau flash giữ cache, bản cứu hộ và kết quả; restore lỗi hoặc bị ngắt giữ recovery, không tự reboot Android. Mở lại app chỉ thu kết quả/phục hồi boot, không chạy lại restore. Phiên chuẩn bị chưa ghi `FLASH_INTENT` được kết thúc khi phục hồi, không ghi boot. Các API/journal định dạng cũ vẫn được giữ; UI Restore nhận ZIP định dạng 3/4/5.

## Engine và hoàn nguyên

Ảnh Android boot v2/gzip/newc được dựng theo operation ID, giữ kernel/DTB và entry không liên quan, thêm guard do recovery init quản lý và cầu GUI action tới native `nandroid`. App dựng lại boot ID/AVB hash descriptor/footer với algorithm NONE; ảnh nguồn không bị sửa.

Guard phiên bản 9 trả boot gốc sớm khi backup Boot và vẫn chịu trách nhiệm hoàn nguyên ở mọi nhánh kết thúc; Stop không cắt ngang cả bước trả boot sớm này.

Guard 9 kiểm tra Stop trực tiếp trong vòng chờ CLI; không tạo rồi gửi TERM cho shell watcher (đã tái hiện lỗi `interrupted` trên mksh Android sau native backup). Guard chỉ trả boot sau xác nhận pull, lỗi, Stop hoặc timeout.

ORS chỉ truyền biến/yêu cầu action. Guard độc lập chịu trách nhiệm trả boot gốc sau engine hoặc lỗi, không đặt bước trả boot sau chuỗi ORS có thể dừng giữa chừng. Cầu ghi status native theo operation ID/phase trước khi action ghi log làm thay đổi status; không nhận kết quả cũ hoặc coi CLI exit 0 là đủ. Danh sách payload phải có tên `.win*` cho các phân vùng đã chọn.

Nếu chưa xác định engine đã dừng, guard dừng recovery và chờ tiến trình kết thúc. Ghi boot thất bại hoặc native còn chạy thì giữ recovery. Restore đã bắt đầu nhưng lỗi thì cũng giữ recovery, không tự phát lại restore. Backup lỗi có thể reboot sau khi trả boot gốc; kết quả backup vẫn là lỗi.

Helper Android có watchdog khoảng 60 giây nếu reboot recovery không diễn ra. Host kiểm tra ownership/kernel boot ID để tránh hai writer. Stop ở màn hình chính luôn yêu cầu xác nhận, mặc định Tiếp tục chạy, cảnh báo brick/bootloop/mất dữ liệu. Xác nhận gắn với operation ID để không dừng nhầm phiên kế tiếp. Guard dừng engine và các writer thuộc phiên trước khi trả boot; restore bị dừng giữ `NeedsRecovery`, không reboot Android. Stop không cắt đoạn ghi/hoàn nguyên boot. Đóng app hoặc mất kết nối không tự phát lệnh cưỡng bức dừng engine. Guard chờ host lấy log tối đa khoảng 60 giây trước reboot, vẫn tự chạy nếu PC mất kết nối.

## Phiên dở dang và cứu hộ

Mở lại cửa sổ cấu hình, chọn phiên dở dang, chọn Kiểm tra / phục hồi boot và đồng ý trong cửa sổ xác nhận dùng chung. App đối chiếu kết quả, không chạy lại native restore. Khi guard đang sở hữu recovery, host không khởi tạo writer thứ hai. Khi Android online, root và quyền sở hữu cho phép, host có thể đẩy bản boot PC rồi `dd` hoàn nguyên; giữ `NeedsRecovery` nếu không làm được.

Đặt `adb` vào PATH trước khi chạy script cứu hộ. `restore-boot.ps1` chỉ dùng ADB khi Android/TWRP còn online, dừng nếu guard/watchdog còn sở hữu writer. Script push original.img, ghi vào đúng boot/slot đã lưu, không tự reboot, unlock, wipe, format hoặc đổi slot. Không phải đường cứu hộ khi thiết bị không vào được Android/TWRP; khi đó cần phương án thủ công riêng theo thiết bị.

Journal lỗi chặn thao tác mới thay vì bỏ qua phiên. Xóa có xác nhận và giới hạn đúng thư mục; phiên dở dang chặn xóa cứu hộ. Bản cứu hộ PC bổ sung được giữ khi xóa catalog.

Chi tiết lệnh: [Báo cáo danh sách lệnh backup](TWRP_BACKUP_COMMANDS.md).

## Kiểm chứng

Build bằng output riêng `bin/PlanCheck`, không thay package. Test service giới hạn trong ba tệp TWRP; không thêm test UI/ViewModel. Baseline trước giai đoạn 2: 865 test, 816 đạt và 49 lỗi có sẵn. Kết quả chạy mới được ghi tại `.artifacts/twrp-build.log`, `.artifacts/twrp-focused.log`, `.artifacts/twrp-results` và `.artifacts/twrp-validation.md`.

```powershell
dotnet build .\DeepDroidChanger.slnx -c Debug --no-restore -m:1 -p:UseSharedCompilation=false -p:OutputPath=bin/PlanCheck/
$env:TEMP = Join-Path (Get-Location) '.artifacts'
$env:TMP = $env:TEMP
& '.\DeepDroidChanger.Tests\bin\PlanCheck\DeepDroidChanger.Tests.exe' --filter 'TestCategory!=DeviceIntegration' --timeout 120s --progress off --no-ansi --report-trx --report-trx-filename twrp.trx --results-directory '.artifacts\twrp-results'
```

Kết quả kiểm chứng ngày 21/10/2026: build **0 warning/error**; **52/52 test TWRP đạt**; bộ đầy đủ **917 test, 868 đạt, 49 lỗi baseline**, không phát sinh lỗi mới. Đã so từng tên/message lỗi với baseline, xác nhận DI và render Light/Dark × Việt/Anh, mô phỏng guard/helper bằng lệnh giả trên PC, kiểm tra Pixel 7a từ chối chỉ đọc. Chi tiết ở `.artifacts/twrp-validation.md`.

## Tệp thay đổi

Sửa 8 tệp tích hợp:

- `DeepDroidChanger/App.xaml.cs`
- `DeepDroidChanger/DeepDroidChanger.csproj`
- `DeepDroidChanger/Resources/Strings/Strings.xaml`
- `DeepDroidChanger/Resources/Strings/Strings.vi.xaml`
- `DeepDroidChanger/Services/Interfaces/IDeviceActionCoordinatorService.cs`
- `DeepDroidChanger/Services/Implementations/DeviceActionCoordinatorService.cs`
- `DeepDroidChanger/ViewModels/ChangeSingleDeviceViewModel.cs`
- `DeepDroidChanger/Views/ChangeSingleDevice/ChangeSingleDeviceView.xaml`

Thêm 28 tệp (giai đoạn 2 và thiết kế lại):

- `DeepDroidChanger/Assets/TWRP/twrp_sargo.img`
- `DeepDroidChanger/Assets/TWRP/guard.sh`
- `DeepDroidChanger/Assets/TWRP/record.sh`
- `DeepDroidChanger/Models/AdbServices/TwrpRecoveryModels.cs`
- `DeepDroidChanger/Services/Interfaces/AdbServices/ITwrpRecoveryService.cs`
- `DeepDroidChanger/Services/Interfaces/DialogServices/ITwrpWorkspaceDialogService.cs`
- `DeepDroidChanger/Services/Implementations/AdbServices/TwrpRecoveryService.cs`
- `DeepDroidChanger/Services/Implementations/AdbServices/TwrpRecoveryService.Archive.cs`
- `DeepDroidChanger/Services/Implementations/AdbServices/TwrpArchiveService.cs`
- `DeepDroidChanger/Services/Implementations/AdbServices/TwrpImageService.cs`
- `DeepDroidChanger/Services/Implementations/AdbServices/TwrpSafety.cs`
- `DeepDroidChanger/Services/Implementations/AdbServices/TwrpSessionStore.cs`
- `DeepDroidChanger/Services/Implementations/AdbServices/TwrpScripts.cs`
- `DeepDroidChanger/Services/Implementations/DialogServices/TwrpWorkspaceDialogService.cs`
- `DeepDroidChanger/ViewModels/Dialogs/TwrpWorkspaceViewModel.cs`
- `DeepDroidChanger/Views/Dialogs/TwrpRecovery/TwrpBackupDialog.xaml`
- `DeepDroidChanger/Views/Dialogs/TwrpRecovery/TwrpBackupDialog.xaml.cs`
- `DeepDroidChanger/Views/Dialogs/TwrpRecovery/TwrpRestoreDialog.xaml`
- `DeepDroidChanger/Views/Dialogs/TwrpRecovery/TwrpRestoreDialog.xaml.cs`
- `DeepDroidChanger/Views/Dialogs/TwrpRecovery/TwrpOptionsView.xaml`
- `DeepDroidChanger/Views/Dialogs/TwrpRecovery/TwrpOptionsView.xaml.cs`
- `DeepDroidChanger/Resources/Strings/Views/TwrpRecovery.xaml`
- `DeepDroidChanger/Resources/Strings/Views/TwrpRecovery.vi.xaml`
- `DeepDroidChanger.Tests/Services/Implementations/AdbServices/TwrpSafetyAndStoreTests.cs`
- `DeepDroidChanger.Tests/Services/Implementations/AdbServices/TwrpImageServiceTests.cs`
- `DeepDroidChanger.Tests/Services/Implementations/AdbServices/TwrpRecoveryServiceTests.cs`
- `docs/TWRP_BACKUP.md`
- `docs/TWRP_BACKUP_COMMANDS.md`

Thiết kế lại thay hai tệp cửa sổ workspace cũ bằng hai cửa sổ cấu hình và một view dùng chung. Không thay test được lưu trong repository trong đợt thiết kế lại, dependency, tài liệu kiến trúc hoặc backup runtime của người dùng. Kiểm chứng đợt mới: `.artifacts/twrp-redesign-report.md`.

## Checklist Pixel 3a thật

Đã chạy backup thực tế trên Pixel 3a được nhận diện bằng `ro.deepdroid.device=sargo`: Data + Super hoàn tất (~6,03 GB), guard trả boot gốc và Android online. Phiên `c90989be8b794d60b316292dba3c2674`; chi tiết trong `.artifacts/pixel3a-live-report.md`. Pixel 7a trước đó chỉ kiểm tra từ chối, không root/flash/reboot.

- [x] Đọc/lưu boot gốc khi Android online; bản PC và bản cứu hộ được tạo trước flash.
- [x] Flash ảnh guard vào boot_a, reboot recovery qua ADB.
- [x] Guard tự khởi động; bản gốc vào RAM trước engine.
- [x] Data đọc được, native Super hoạt động; engine nhận diện EFS/modem/persist (chưa backup các mục nhạy cảm).
- [x] Backup Data + Super hoàn tất; guard hoàn nguyên boot và reboot Android.
- [x] Restore Data + Super từ bản PC trên thiết bị thật: phiên `c1bdce9f0cc9466da28f749f187c07d4`, native hoàn tất 383 giây, guard trả boot gốc, Android online; chi tiết `.artifacts/pixel3a-restore-report.md`.
- [ ] Restore lỗi/gián đoạn giữ recovery trên thiết bị thật (chưa cố ý gây lỗi).
- [ ] Mất ADB/đóng app, app mở lại và xử lý journal, không phát lại restore.
- [ ] Watchdog khi không chuyển recovery và tránh hai writer cùng ghi boot.
- [ ] Cứu hộ ADB từ PC khi còn online; đánh giá tình huống mất nguồn/không vào recovery riêng.

Lịch sử kiểm chứng lượt năm lựa chọn/Media: `.artifacts/twrp-media-report.md`. Media đã được gỡ; kiểm chứng bốn lựa chọn hiện tại nằm trong `.artifacts/twrp-four-options-report.md`. Không thao tác thiết bị thật trong lượt sửa này.

Kiểm chứng Backup CLI: `.artifacts/twrp-cli-report.md`. Không chạy thêm backup/restore hoặc thao tác thiết bị thật trong lượt này.

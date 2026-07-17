# DeepDroidChanger

DeepDroidChanger là ứng dụng WPF trên Windows để quản lý và thay đổi cấu hình thiết bị Android qua ADB. Dự án sử dụng .NET 10, MVVM, dependency injection và service abstraction; Android Platform Tools cùng scrcpy được đóng gói trong `DeepDroidChanger/Assets/Tools`.

> [!WARNING]
> **Change Device là thao tác phá hủy dữ liệu.** Flow mặc định xóa dữ liệu package, tài khoản và nhiều trạng thái hệ thống trước khi áp dụng profile mới rồi reboot. Chỉ dùng trên thiết bị đã backup, có ADB root và ROM/module tương thích.

## Chức năng hiện có

### Đăng nhập và giao diện

- Đăng nhập bằng Amazon Cognito SRP trước khi mở cửa sổ chính.
- Ghi nhớ tài khoản theo lựa chọn; mật khẩu được bảo vệ bằng Windows DPAPI `CurrentUser`.
- Token phiên chỉ tồn tại trong bộ nhớ, không được ghi xuống cấu hình.
- Sidebar có thể thu gọn; hỗ trợ Light/Dark, tiếng Anh/tiếng Việt và Per-Monitor DPI.
- Điều hướng giữa Device Manager và Settings.

### Quản lý thiết bị

- Dò thiết bị bằng ADB và polling trạng thái `Online`, `Offline`, `Unauthorized`.
- Thêm nhiều thiết bị từ danh sách ADB, gán loại thiết bị và lưu theo serial.
- Chọn một thiết bị tại một thời điểm; lọc theo trạng thái kết nối hoặc Active/Inactive.
- Sửa Name/Type trực tiếp trong bảng, lưu thiết bị đang chọn và tỉ lệ cột.
- Reboot, xem thông tin, mở Device Viewer và xóa thiết bị có xác nhận.
- Hiển thị trạng thái/tiến trình của action trên từng dòng thiết bị.

### Random Device và SIM

- Lấy profile ngẫu nhiên từ Device Info API theo Brand, Android version, Country và Carrier.
- Chuẩn hóa identity gồm fingerprint/build, serial, Android ID, IMEI, IMSI, ICCID, số điện thoại, Wi-Fi/Bluetooth MAC và thông tin SIM.
- Dữ liệu cục bộ dùng cho việc tạo profile nằm trong `Assets/Data`: carrier, timezone, TAC IMEI, MAC vendor, tên và word list.
- **Random Device** và **Random SIM** chỉ cập nhật form. Dữ liệu chỉ được ghi lên điện thoại khi chạy **Change Device**.

### Change Device

Nút **Change Device** chỉ khả dụng khi đã chọn thiết bị `Online` và đã tạo được một profile Random Device. Trước khi thực thi, ứng dụng luôn hiển thị dialog mô tả các thông tin sẽ đổi và dữ liệu sẽ bị xóa; người dùng phải xác nhận mới tiếp tục.

Flow hiện tại:

1. Kiểm tra thiết bị online và profile hợp lệ.
2. Hiển thị dialog xác nhận với đúng cấu hình Default/Advanced hiện tại.
3. Khởi động lại `adbd` ở quyền root, chờ thiết bị và xác minh `id -u = 0`.
4. Tắt Wi-Fi và đọc Android ID hiện tại để đối chiếu sau khi thay đổi.
5. `SetPropertyAsync` chỉ bật bypass theo phạm vi một lệnh khi property bắt đầu bằng `ro.`, rồi luôn tắt bypass ngay sau `setprop`.
6. Xóa data cũ theo policy đã chọn.
7. Áp dụng build/device/SIM/MAC/Android ID và Android settings từ profile.
8. Đồng bộ dữ liệu, reboot và chờ `sys.boot_completed = 1`.
9. Đọc lại Android ID để xác minh kết quả; lỗi ở bước bắt buộc sẽ không được báo thành công giả.

Mỗi serial có lock riêng để tránh hai Change Device chạy đồng thời trên cùng thiết bị.

#### Chế độ Default

- Áp dụng toàn bộ profile mới, gồm Android ID, MAC Wi-Fi/Bluetooth và SIM.
- Lấy danh sách package đã cài rồi xóa data bằng `pm clear`; giữ nguyên APK.
- Xóa dữ liệu các package Google/account liên quan.
- Xóa file account và nội dung file dưới `/data/system_ce` và `/data/system_de`, nhưng giữ lại cây thư mục để Android có thể tái sử dụng đúng path/permission.
- Xóa database hệ thống khớp `/data/system/*.db*`, trừ các file registry bắt đầu bằng `/data/system/package`.

#### Chế độ Advanced

- Chọn có đổi Android ID, MAC và SIM hay không.
- Chọn xóa toàn bộ package, package Google hoặc danh sách package cụ thể.
- Chọn riêng việc xóa account Google và CE/DE state.
- Mặc định vẫn dùng `pm clear`. Tùy chọn `rm -rf` chỉ áp dụng cho data của package được chọn và xóa tám path package/data/ART profile đã định nghĩa trong service.

Ở cả Default và Advanced, cleanup residual vẫn chạy: ứng dụng dùng `find ... -not -type d -delete` để xóa file bên trong các vùng log/cache/state nhưng giữ lại directory root. Cách này giải quyết trường hợp một số thư mục hệ thống không được tạo lại đúng khi xóa cả path. `/data/misc/bluetooth` và `/data/misc/bluedroid` vẫn nằm trong danh sách cleanup (xóa nội dung nhưng giữ root); package registry, `/data/app`, kho `/data/property/persistent_properties` và dữ liệu Wi-Fi APEX được bảo vệ khỏi danh sách cleanup.

Cleanup được hợp nhất thành một shell script và truyền thẳng qua standard input của `adb -s <serial> shell sh`; script không nằm trong Windows command line và không cần push tệp `.sh` lên thiết bị. Khi cần xóa package, tổng số ADB process của cleanup là một lần list package và một lần chạy script.

### Location và Timezone

- Nhập latitude/longitude hoặc lấy vị trí theo IP thiết bị; validate trước khi áp dụng.
- Chọn timezone IANA hoặc xác định theo IP thiết bị.
- Lưu và khôi phục cấu hình riêng theo serial.
- Chỉ áp dụng thay đổi sau khi người dùng xác nhận dialog.

### SOCKS5 Proxy

- Nhập proxy dạng đầy đủ hoặc theo host, port, username và password.
- Validate endpoint/credential, start/stop proxy theo thiết bị.
- Có thể đổi location và timezone theo IP proxy sau khi kết nối.
- Rollback proxy khi workflow sau kết nối thất bại.

### Update Integrity

- Cập nhật PIF/Integrity JSON và Keybox XML độc lập hoặc cùng lúc.
- Hỗ trợ nguồn server và tệp cục bộ; kiểm tra file trước khi push.
- Lưu lựa chọn theo thiết bị và xử lý đường dẫn đã lưu không còn tồn tại.

### Cài APK/XAPK

- Hàng đợi nhiều APK/XAPK, trạng thái từng file, progress và cancellation.
- Tùy chọn auto-grant permission và cho phép downgrade.
- Hỗ trợ split APK, OBB và dọn thư mục giải nén tạm.
- Chặn path traversal trong XAPK và ánh xạ các lỗi ADB/install thường gặp.

### Device Viewer

- Mở và quản lý phiên scrcpy riêng theo thiết bị.
- Resize giữ đúng tỉ lệ, reconnect/restart stream và dừng process khi đóng.
- Back, Home, Recent, Power, Volume, Enter, screenshot, gửi text và chạy ADB shell.

## Chức năng chưa hoàn thiện

Các surface sau vẫn là placeholder hoặc mới chỉ có phần tạo dữ liệu form; chúng không thực thi flow ADB hoàn chỉnh:

- Random & Change Device.
- Change SIM độc lập.
- Flash ROM.
- Advanced Settings; trang Settings hiện chủ yếu quản lý theme/language và là workspace cho tính năng tiếp theo.

## Yêu cầu

- Windows 10/11.
- .NET 10 SDK và PowerShell để phát triển.
- Kết nối mạng cho Cognito, Device Info API và IP geolocation.
- USB debugging và ADB authorization cho các action thông thường.
- **ADB root thực sự** (`adb root` và `id -u` trả `0`) cùng ROM/module hiểu các property spoof cho Change Device.

Khả năng thiết bị xuất hiện trong `adb devices` không đảm bảo ROM hỗ trợ mọi workflow. Không dùng Change Device trên máy chính hoặc thiết bị còn dữ liệu chưa backup.

## Build và chạy

Từ thư mục gốc repository:

```powershell
dotnet restore .\DeepDroidChanger.slnx
dotnet build .\DeepDroidChanger.slnx -c Debug
dotnet run --project .\DeepDroidChanger\DeepDroidChanger.csproj
```

Build Release:

```powershell
dotnet build .\DeepDroidChanger.slnx -c Release
```

## Kiểm thử

```powershell
dotnet test .\DeepDroidChanger.slnx --no-build --no-restore
powershell -ExecutionPolicy Bypass -File .\DeepDroidChanger.Tests\verify-coverage.ps1
```

Test suite dùng fake/mock cho Cognito, HTTP API và ADB. Việc xác nhận tương thích ROM, quyền root và hành vi cleanup cuối cùng vẫn cần test có kiểm soát trên thiết bị thật.

## Dữ liệu runtime và bảo mật

Các file runtime nằm cạnh executable trong `Settings/`; thư mục output `bin/` đã được Git ignore.

| Tệp | Nội dung |
| --- | --- |
| `Settings/settings.json` | Theme, ngôn ngữ, sidebar, thiết bị đang chọn, tỉ lệ cột và cấu hình action |
| `Settings/devices.json` | Danh sách thiết bị và cấu hình riêng theo serial |
| `Settings/account.json` | Username và mật khẩu đã mã hóa khi bật Remember account |

- JSON được ghi atomically; file hỏng được quarantine trước khi tạo cấu hình mặc định.
- `account.json` chỉ giải mã được bởi cùng Windows user đã tạo file.
- Không commit runtime data, credential, token, file tạm hoặc dữ liệu lấy từ thiết bị.

## Kiến trúc

```text
View -> ViewModel -> Workflow/Service -> ADB/API/Persistence -> Typed result -> ViewModel
```

- `Views`: binding và UI event thuần túy.
- `ViewModels`: state, validation và command.
- `Services`: ADB/process, HTTP, file I/O, persistence và orchestration.
- `Models`: POCO và typed result, không chứa I/O.
- Dependency được đăng ký tập trung trong `App.xaml.cs` bằng `Microsoft.Extensions.Hosting`.
- Service hạ tầng/main workspace dùng singleton; dialog và dialog ViewModel dùng transient.

## Cấu trúc repository

```text
DeepDroidChanger/
├── DeepDroidChanger/                 # Ứng dụng WPF
│   ├── Views/                        # Feature view và dialog
│   ├── ViewModels/                   # State, validation, command
│   ├── Services/                     # Interface và implementation
│   ├── Models/                       # POCO và typed result
│   ├── Resources/                    # Localization và XAML theme
│   └── Assets/                       # Data, icons và bundled tools
├── DeepDroidChanger.Tests/           # MSTest, fake/mock và architecture tests
├── docs/                             # Rule và tài liệu reverse engineering
└── DeepDroidChanger.slnx
```

Tài liệu liên quan:

- [`AGENTS.md`](AGENTS.md), [`docs/DESIGN.md`](docs/DESIGN.md), [`docs/THEMES.md`](docs/THEMES.md), [`docs/GIT.md`](docs/GIT.md): quy tắc bắt buộc của repository.
- [`docs/MiChangerPlus_Dump.md`](docs/MiChangerPlus_Dump.md): kết quả dump/phân tích tĩnh MiChangerPlus dùng làm nguồn đối chiếu hành vi.

## Công nghệ chính

- WPF trên .NET 10.
- CommunityToolkit.Mvvm.
- Microsoft.Extensions.Hosting và dependency injection.
- Material Design in XAML Toolkit.
- MSTest và NSubstitute.
- Amazon Cognito authentication.
- ADB, fastboot, scrcpy và SOCKS5 proxy integration.

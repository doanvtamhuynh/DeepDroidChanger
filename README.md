# DeepDroidChanger

DeepDroidChanger là ứng dụng WPF trên Windows để quản lý và thay đổi cấu hình thiết bị Android qua ADB. Dự án sử dụng .NET 10, MVVM, dependency injection và service abstraction; Android Platform Tools được đóng gói trong `DeepDroidChanger/Assets/Tools`.

> [!WARNING]
> **Change Device là thao tác phá hủy dữ liệu.** Flow mặc định xóa dữ liệu package, tài khoản và nhiều trạng thái hệ thống trước khi áp dụng profile mới rồi reboot. Chỉ dùng trên thiết bị đã backup, có ADB root và ROM/module tương thích.

## Chức năng hiện có

### Đăng nhập và giao diện

- Đăng nhập bằng Amazon Cognito SRP trước khi mở cửa sổ chính.
- Ghi nhớ tài khoản theo lựa chọn; mật khẩu được bảo vệ bằng Windows DPAPI `CurrentUser`.
- Token phiên chỉ tồn tại trong bộ nhớ, không được ghi xuống cấu hình.
- Sidebar có thể thu gọn; hỗ trợ Light/Dark, tiếng Anh/tiếng Việt và Per-Monitor DPI.
- Điều hướng giữa Devices Manager và Settings.

### Quản lý thiết bị

- Dò thiết bị bằng ADB và polling trạng thái `Online`, `Offline`, `Unauthorized`.
- Thêm nhiều thiết bị từ danh sách ADB, gán loại thiết bị và lưu theo serial.
- Chọn một thiết bị tại một thời điểm; lọc theo trạng thái kết nối hoặc Active/Inactive.
- Sửa Name/Type trực tiếp trong bảng, lưu thiết bị đang chọn và tỉ lệ cột.
- Reboot, xem thông tin và xóa thiết bị có xác nhận.
- Hiển thị trạng thái/tiến trình của action trên từng dòng thiết bị.

### Random Device và SIM

- Lấy profile ngẫu nhiên từ Device Info API theo Brand, Android version, Country và Carrier.
- Chuẩn hóa identity gồm fingerprint/build, serial, IMEI, IMSI, ICCID, số điện thoại, Wi-Fi/Bluetooth MAC và thông tin SIM.
- Dữ liệu cục bộ dùng cho việc tạo profile nằm trong `Assets/Data` và được nhúng vào assembly khi build: carrier, timezone, TAC IMEI, MAC vendor, tên và word list.
- **Random Device** và **Random SIM** chỉ cập nhật form. Dữ liệu chỉ được ghi lên điện thoại khi chạy **Change Device**.

### Change Device

Nút **Change Device** chỉ khả dụng khi đã chọn thiết bị `Online` và đã tạo được một profile Random Device. Trước khi thực thi, ứng dụng luôn hiển thị dialog mô tả các thông tin sẽ đổi và dữ liệu sẽ bị xóa; người dùng phải xác nhận mới tiếp tục.

Flow hiện tại:

1. Kiểm tra thiết bị online và profile hợp lệ.
2. Hiển thị dialog xác nhận với đúng cấu hình Default/Advanced hiện tại.
3. Khởi động lại `adbd` ở quyền root, chờ thiết bị và xác minh `id -u = 0`.
4. Tắt Wi-Fi; tắt thêm Bluetooth khi đổi MAC; chỉ đọc Android ID hiện tại khi Advanced bật **Change Android ID**.
5. `SetPropertyAsync` chỉ bật bypass theo phạm vi một lệnh khi property bắt đầu bằng `ro.`, rồi luôn tắt bypass ngay sau `setprop`.
6. Áp dụng build/device/SIM/MAC và Android settings từ profile; không ghi Android ID thủ công.
7. Xóa data cũ theo policy đã chọn, gồm shared identity và `settings_ssaid.xml`; chỉ chạy `settings delete secure android_id` khi Advanced bật **Change Android ID**.
8. Đồng bộ dữ liệu, reboot và chờ `sys.boot_completed = 1`.
9. Đọc lại các property/name đã đổi; khi đổi Android ID được chọn thì xác minh ID mới tồn tại và khác ID cũ. Lỗi ở bước bắt buộc sẽ không được báo thành công giả.

Mỗi serial có lock riêng để tránh hai Change Device chạy đồng thời trên cùng thiết bị.

#### Chế độ Default

- Áp dụng profile mới, gồm MAC Wi-Fi/Bluetooth và SIM, nhưng giữ nguyên Android ID.
- Lấy danh sách package đã cài trên host rồi chạy riêng `force-stop` và `pm clear --user 0` cho từng package; giữ nguyên APK và bản update trong `/data/app`.
- Xóa đúng các database account CE/DE, sync state và registered-service state; không xóa toàn bộ `/data/system_ce`, `/data/system_de` hoặc database hệ thống.
- Giữ nguyên Wi-Fi APEX/legacy/CE/DE để không mất mạng đã lưu; reset Bluetooth, DHCP/network, radio/carrier/APN, usage/net/process/graphics stats, ANR, tombstone và SSAID.

#### Chế độ Advanced

- Chọn có đổi Android ID, MAC và SIM hay không. Android ID chỉ được tạo lại khi bật **Change Android ID**.
- Chọn xóa toàn bộ package, package Google hoặc danh sách package cụ thể.
- Chọn riêng việc xóa account Google và CE/DE state.
- Package được chọn luôn được xử lý bằng `pm clear --user 0`. Khi bật **Dùng chế độ xóa sâu cho package**, cleanup xóa file còn sót trong sáu path app-data/media/ART profile nhưng giữ nguyên cây thư mục, owner, mode và SELinux context.

**Change without Wipe** không chạy cleanup package/account/shared identity. Action này vẫn xóa `settings_ssaid.xml`; chỉ xóa secure Android ID và xác minh ID mới khi option Advanced được chọn. **Wipe without Change** chỉ chạy package/account policy đã chọn, giữ SSAID và toàn bộ shared identity để không làm thay đổi profile. **Random Change & Wipe** dùng cùng option như **Change Device**.

Full Change áp dụng profile trước rồi mới cleanup. Sau reboot, ứng dụng đọc lại brand, manufacturer, model, device code, product name, release, fingerprint, build ID, security patch, device name và Bluetooth name; nếu còn giá trị cũ thì action báo lỗi thay vì Completed.

Cleanup không ghép hàng trăm package/path vào một command dài. Tool chỉ xử lý Android user `0`, nên toàn bộ account/identity path dùng trực tiếp user `0` và không cần gọi `pm list users`; shell không còn loop `for target` hoặc wildcard ở directory segment. Chỉ một wildcard cuối tên file được phép để bắt SQLite sidecar/backup trong cùng directory. Mỗi `force-stop`, `pm clear`, file target hoặc directory target được gửi thành một lệnh `adb shell sh` riêng và được chờ tuần tự với timeout độc lập 60 giây. Exit code và timeout được ghi warning theo loại thao tác, nhưng cleanup vẫn tiếp tục lệnh kế tiếp và không retry; output/raw command không được ghi log. Cancellation của toàn workflow vẫn được propagate. Directory hệ thống chỉ bị xóa file con bằng `find ... -not -type d -delete`; các root nhạy cảm như `/data/app`, `/data/system*`, `/data/misc*`, `/data/vendor*`, `/data/apex`, `/data/property`, `/data/local/tmp` và bốn vùng dữ liệu Wi-Fi legacy/APEX/CE/DE bị guard bảo vệ khỏi xóa trực tiếp.

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

Mỗi project .NET có thư mục build output riêng. Với project WPF này và cấu hình
mặc định hiện tại:

- Debug executable:
  `.\DeepDroidChanger\bin\Debug\net10.0-windows\DeepDroidChanger.exe`
- Release executable:
  `.\DeepDroidChanger\bin\Release\net10.0-windows\DeepDroidChanger.exe`

`bin/<Configuration>/<TargetFramework>/` là cấu trúc output của project
`DeepDroidChanger.csproj`, không phải một thư mục dữ liệu riêng của ứng dụng và
không phải output dùng chung tại solution root.

## Kiểm thử

```powershell
dotnet test .\DeepDroidChanger.slnx --no-build --no-restore
powershell -ExecutionPolicy Bypass -File .\DeepDroidChanger.Tests\verify-coverage.ps1
```

Test suite dùng fake/mock cho Cognito, HTTP API và ADB. Việc xác nhận tương thích ROM, quyền root và hành vi cleanup cuối cùng vẫn cần test có kiểm soát trên thiết bị thật.

## Dữ liệu runtime và bảo mật

Trong tài liệu này, **thư mục ứng dụng** là thư mục đang chứa
`DeepDroidChanger.exe`. Ứng dụng lấy đường dẫn này từ
`AppContext.BaseDirectory`, sau đó tạo `AppSettings/`, `ChangeSingleDevice/` và
`ChangeMultipleDevices/` trực
tiếp bên trong. Vì vậy khi chạy bản Debug vừa build, cấu trúc thực tế là:

```text
DeepDroidChanger/bin/Debug/net10.0-windows/
├── DeepDroidChanger.exe
├── AppSettings/
│   ├── app_settings.json
│   └── account.json
├── ChangeSingleDevice/
    ├── devices.json
│   └── <serial>/
        ├── random_config.json
        ├── change_options_config.json
        ├── update_integrity_config.json
        ├── location_config.json
        ├── timezone_config.json
│       └── proxy_config.json
└── ChangeMultipleDevices/
    ├── change_config.json
    └── change_options_config.json
```

Nếu executable được publish hoặc chuyển sang thư mục khác, hai thư mục dữ liệu
trên cũng được tạo cạnh executable tại vị trí mới. Chúng được tạo khi ứng dụng
chạy và service persistence được gọi; riêng thao tác `dotnet build` không tạo
các file runtime này.

| Tệp | Nội dung |
| --- | --- |
| `AppSettings/app_settings.json` | Theme, ngôn ngữ, sidebar, thiết bị đang chọn, tỉ lệ cột và cấu hình action |
| `ChangeSingleDevice/devices.json` | Index thiết bị, chỉ gồm serial, name, type và dataPath |
| `ChangeSingleDevice/<serial>/random_config.json` | Brand, Android version, quốc gia, nhà mạng, tùy chọn random SIM và Integrity security patch |
| `ChangeSingleDevice/<serial>/change_options_config.json` | Tùy chọn Change Device và cleanup package |
| `ChangeSingleDevice/<serial>/update_integrity_config.json` | Cấu hình dialog Update Integrity |
| `ChangeSingleDevice/<serial>/location_config.json` | Cấu hình dialog Change Location |
| `ChangeSingleDevice/<serial>/timezone_config.json` | Cấu hình dialog Change Timezone |
| `ChangeSingleDevice/<serial>/proxy_config.json` | Cấu hình dialog Fake Proxy |
| `ChangeMultipleDevices/change_config.json` | Cấu hình thay đổi áp dụng cho nhiều thiết bị |
| `ChangeMultipleDevices/change_options_config.json` | Tùy chọn Change Device cho nhiều thiết bị |
| `AppSettings/account.json` | Username và mật khẩu đã mã hóa khi bật Remember account |

Thời điểm persistence được giữ theo hành vi của ứng dụng:

- Add/Delete Device ghi index và tạo/xóa thư mục thiết bị ngay khi thao tác hoàn tất.
- Name được tự lưu sau debounce 300 ms; Type được lưu ngay. Các edit còn chờ
  được flush khi rời Change Single Device.
- Brand, Android version, country, carrier và Change SIM được tự lưu sau
  debounce 300 ms vào `random_config.json`. Device profile đầy đủ vừa random
  chỉ tồn tại trong phiên chạy để thực hiện Change Device, không được ghi vào
  config.
- Advanced Change Options chỉ ghi kết quả sau khi người dùng xác nhận dialog.
- Location, Timezone và Proxy tự lưu cấu hình hợp lệ khi chỉnh; nút Save hoặc
  thao tác đóng dialog chờ hoàn tất mọi lượt ghi còn pending.
- Update Integrity tự lưu mỗi thay đổi cấu hình và ghi lại kết quả cuối khi
  người dùng xác nhận thực hiện.

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
- ADB, fastboot và SOCKS5 proxy integration.

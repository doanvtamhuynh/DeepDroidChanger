# DeepDroidChanger

DeepDroidChanger là ứng dụng desktop WPF dành cho Windows, dùng để quản lý và thực hiện các workflow cấu hình trên thiết bị Android thông qua ADB. Dự án được tái cấu trúc từ phiên bản DeepDroidChanger cũ, giữ lại luồng sử dụng chính nhưng chuyển sang kiến trúc MVVM, dependency injection và service abstraction để dễ kiểm thử, bảo trì và mở rộng.

Ứng dụng tích hợp sẵn Android Platform Tools và scrcpy trong thư mục `Assets/Tools`, hỗ trợ giao diện Light/Dark, tiếng Anh/tiếng Việt và Per-Monitor DPI.

## Chức năng chính

### Đăng nhập và phiên làm việc

- Hiển thị cửa sổ đăng nhập trước khi mở giao diện chính; hủy đăng nhập sẽ đóng ứng dụng.
- Xác thực tài khoản bằng Amazon Cognito SRP.
- Hiển thị/ẩn mật khẩu, kiểm tra dữ liệu nhập và ánh xạ lỗi đăng nhập thành thông báo thân thiện.
- Tùy chọn ghi nhớ tài khoản. Mật khẩu được bảo vệ bằng Windows DPAPI với phạm vi `CurrentUser`.
- Token xác thực chỉ được giữ trong bộ nhớ trong thời gian chạy, không ghi xuống tệp cấu hình.

### Giao diện chính

- Sidebar có thể thu gọn/mở rộng và ghi nhớ trạng thái giữa các lần chạy.
- Điều hướng giữa Device Manager và Settings.
- Chuyển đổi Light/Dark theme ngay khi ứng dụng đang chạy.
- Chuyển đổi tiếng Anh/tiếng Việt bằng resource dictionary.
- Giao diện responsive, hỗ trợ resize và DPI PerMonitorV2.

### Quản lý thiết bị

- Tự động dò thiết bị qua ADB và polling trạng thái kết nối.
- Phân biệt thiết bị online, offline và unauthorized.
- Thêm thiết bị mới từ danh sách ADB, chọn nhiều thiết bị và gán loại thiết bị trước khi lưu.
- Xóa thiết bị có hộp thoại xác nhận.
- Lọc theo trạng thái kết nối hoặc trạng thái Active/Inactive.
- Chọn một thiết bị tại một thời điểm; ghi nhớ thiết bị đang chọn.
- Chỉnh sửa trực tiếp Name và Type trong DataGrid.
- Thay đổi kích thước cột và lưu tỉ lệ cột cho lần chạy tiếp theo.
- Context menu cho xem thiết bị, xem thông tin, reboot và xóa.
- Hiển thị tiến trình/trạng thái của từng thiết bị ngay trong bảng.

### Hồ sơ thiết bị và Random Device

- Quản lý Brand, Android version, Country và Carrier theo MCC/MNC.
- Đọc dữ liệu carrier, timezone, MAC vendor và dữ liệu tạo ngẫu nhiên từ `Assets/Data`.
- Gọi Device Info API bằng phiên đã xác thực để lấy profile thiết bị ngẫu nhiên.
- Tạo và chuẩn hóa IMEI, IMSI, ICCID, Wi-Fi MAC, serial, số điện thoại và thông tin SIM.
- Random Device chỉ cập nhật form trong phiên hiện tại; không tự động áp dụng profile lên điện thoại.

### Thay đổi vị trí

- Nhập latitude/longitude thủ công hoặc lấy vị trí theo IP của thiết bị.
- Kiểm tra phạm vi và định dạng tọa độ trước khi thực thi.
- Lưu cấu hình riêng cho từng thiết bị.
- Chỉ áp dụng thay đổi qua ADB sau khi người dùng xác nhận.

### Thay đổi múi giờ

- Chọn múi giờ từ dữ liệu IANA và tìm kiếm theo tên quốc gia, khu vực hoặc timezone ID.
- Dùng cấu hình đã chọn hoặc tự xác định theo IP thiết bị.
- Khôi phục lựa chọn đã lưu cho từng thiết bị và áp dụng qua ADB.

### SOCKS5 Proxy

- Nhập proxy ở dạng đầy đủ hoặc theo từng trường host, port, username và password.
- Kiểm tra cú pháp, port và credential trước khi kết nối.
- Start/stop proxy theo từng thiết bị.
- Có thể tự động đổi location và timezone theo IP proxy sau khi kết nối thành công.
- Có rollback và thông báo lỗi khi proxy hoặc workflow sau kết nối thất bại.

### Update Integrity

- Cập nhật Integrity/PIF và Keybox độc lập hoặc cùng lúc.
- Hỗ trợ nguồn trực tuyến và tệp cục bộ.
- Chọn PIF JSON và Keybox XML bằng file picker.
- Kiểm tra tệp, kích thước và nội dung XML trước khi gửi sang thiết bị.
- Ghi nhớ lựa chọn riêng theo thiết bị và tự phục hồi khi đường dẫn tệp đã lưu không còn tồn tại.

### Cài APK/XAPK

- Thêm nhiều APK/XAPK vào hàng đợi, xóa item và theo dõi trạng thái từng tệp.
- Tùy chọn tự cấp quyền ứng dụng và cho phép downgrade.
- Hiển thị progress, hỗ trợ hủy và tổng hợp kết quả cài đặt.
- Xử lý split APK và OBB trong XAPK.
- Chống path traversal khi giải nén XAPK và luôn dọn thư mục tạm.
- Ánh xạ các lỗi thường gặp: APK không hợp lệ, sai ABI, version downgrade, thiếu dung lượng và lỗi ADB.

### Device Viewer

- Mở phiên scrcpy theo từng thiết bị và tự quản lý vòng đời process/session.
- Giữ đúng tỉ lệ màn hình khi resize và hỗ trợ Per-Monitor DPI.
- Reconnect/restart phiên stream khi cần.
- Các phím điều khiển Back, Home, Recent, Power, Volume Up/Down và Enter.
- Chụp ảnh màn hình, gửi text và chạy lệnh ADB shell.
- Dừng sạch stream/process khi đóng cửa sổ.

## Chức năng hiện chưa triển khai

Các surface sau được giữ lại để bảo toàn luồng giao diện của dự án cũ nhưng hiện chỉ hiển thị thông báo, không báo thành công giả và không chạy ADB:

- Change Device.
- Random & Change Device.
- Random SIM.
- Change SIM.
- Flash ROM.
- Advanced Settings; màn hình Settings hiện là workspace placeholder.

## Luồng sử dụng cơ bản

1. Bật Developer options và USB debugging trên thiết bị Android.
2. Kết nối thiết bị với máy tính và chấp nhận RSA authorization nếu được hỏi.
3. Chạy DeepDroidChanger và đăng nhập bằng tài khoản hợp lệ.
4. Mở Device Manager, chọn **Add New Devices** để lưu thiết bị cần quản lý.
5. Chọn một thiết bị trong DataGrid rồi sử dụng các thao tác profile, location, timezone, proxy, integrity, package installation hoặc viewer.

Một số thao tác chuyên sâu phụ thuộc ROM, quyền root và các thành phần được cài trên thiết bị. Khả năng thiết bị xuất hiện trong `adb devices` không đảm bảo mọi workflow đều được ROM đó hỗ trợ.

## Yêu cầu phát triển

- Windows 10 hoặc Windows 11.
- .NET 10 SDK.
- PowerShell.
- Kết nối mạng cho Cognito, Device Info API và IP geolocation.
- Thiết bị Android có ADB authorization để chạy các chức năng liên quan đến thiết bị thật.

ADB, fastboot và scrcpy cần thiết cho ứng dụng đã được đóng gói trong `DeepDroidChanger/Assets/Tools/platform-tools` và được copy sang output khi build.

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

Chạy toàn bộ test:

```powershell
dotnet test .\DeepDroidChanger.slnx --no-build --no-restore
```

Chạy package vulnerability audit và coverage gate:

```powershell
powershell -ExecutionPolicy Bypass -File .\DeepDroidChanger.Tests\verify-coverage.ps1
```

Coverage gate hiện yêu cầu tối thiểu 70% line coverage, 55% branch coverage và 80% line coverage cho các service/ViewModel trọng yếu. Test suite không gọi Cognito, HTTP API hoặc ADB thật; các dependency bên ngoài được thay bằng fake/mock.

## Dữ liệu runtime và bảo mật

Các tệp runtime nằm cạnh executable trong thư mục `Settings/` và đã được loại khỏi Git:

| Tệp | Nội dung |
| --- | --- |
| `Settings/settings.json` | Theme, ngôn ngữ, trạng thái sidebar, thiết bị đang chọn và tỉ lệ cột |
| `Settings/devices.json` | Danh sách thiết bị và cấu hình riêng theo serial |
| `Settings/account.json` | Username và mật khẩu đã mã hóa khi bật Remember account |

- JSON được ghi atomically để giảm nguy cơ mất dữ liệu khi ứng dụng bị đóng giữa lúc ghi.
- Tệp cấu hình hỏng được chuyển sang tên quarantine trước khi ứng dụng tạo cấu hình mặc định.
- `account.json` chỉ giải mã được bởi cùng Windows user đã tạo tệp.
- Không commit thư mục `Settings/`, token, credential hoặc dữ liệu thiết bị runtime.

## Kiến trúc

Ứng dụng tuân theo luồng:

```text
View -> ViewModel -> Workflow/Service -> ADB/API/Persistence -> Typed result -> ViewModel
```

- **Views** chỉ chứa binding và UI event thuần túy.
- **ViewModels** quản lý state, validation và command.
- **Services** xử lý ADB, process, HTTP, file I/O, persistence và orchestration.
- **Models** là POCO/typed result, không chứa I/O hoặc service call.
- Dependency được đăng ký tập trung trong `App.xaml.cs` bằng `Microsoft.Extensions.Hosting`.
- Singleton được dùng cho settings, session, hạ tầng và main workspace; dialog/ViewModel của dialog dùng transient lifetime.

Quy tắc kiến trúc, theme và Git nằm trong:

- `AGENTS.md`
- `docs/DESIGN.md`
- `docs/THEMES.md`
- `docs/GIT.md`

## Cấu trúc repository

```text
DeepDroidChanger/
├── DeepDroidChanger/                 # Ứng dụng WPF
│   ├── Views/                        # Main feature và dialog
│   ├── ViewModels/                   # State, validation, command
│   ├── Services/                     # Interface và implementation
│   ├── Models/                       # POCO và typed result
│   ├── Resources/                    # Localization và XAML theme
│   └── Assets/                       # Data, icons và bundled tools
├── DeepDroidChanger.Tests/           # MSTest, fake/mock và architecture tests
├── docs/                             # Architecture, theme và Git rules
└── DeepDroidChanger.slnx
```

## Công nghệ chính

- WPF trên .NET 10.
- CommunityToolkit.Mvvm.
- Microsoft.Extensions.Hosting và dependency injection.
- Material Design in XAML Toolkit.
- MSTest và NSubstitute.
- Amazon Cognito authentication.
- ADB, fastboot, scrcpy và SOCKS5 proxy integration.

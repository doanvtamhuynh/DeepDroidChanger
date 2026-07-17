# MiChangerPlus — Dump và phân tích chức năng

## 1. Phạm vi và độ tin cậy

- Binary: `G:\MiChangerPlus\MiChangerPlus.exe`
- Product/version: `MiChangerPlus 5.4.0`, company `cpidng`
- Kích thước: `57,558,016` bytes
- SHA-256: `B6A23120F9F45A83D3494332EDF72A23897E37226BA2F6F85C2BEADF3001734C`
- Chữ ký Authenticode: không có.
- Dạng binary: Windows x64 .NET NativeAOT (`DotNetRuntimeDebugHeader`, `.managedcode$*`), không còn metadata IL chuẩn để mở bằng ILSpy.

Phân tích được thực hiện tĩnh bằng chuỗi nhúng, PE/x64 call graph và pseudocode Ghidra sau khi rehydrate metadata NativeAOT. Không chạy chương trình và không gửi lệnh xóa tới thiết bị ADB. Tên `FUN_...` là địa chỉ native do mất tên C# gốc; tên mô tả trong tài liệu là nhãn suy luận từ call graph và lệnh được gọi.

Tài liệu này là bản ghi từ dump/reverse engineering của đúng binary có hash ở trên, không phải source code chính thức của nhà phát triển. Các mục ghi “đã xác nhận” có command string và call path tương ứng; tên helper hoặc mục đích UI không còn symbol gốc được đánh dấu là suy luận.

## 2. Danh mục chức năng của ứng dụng

### 2.1. Thiết bị và kết nối

| Chức năng | Hành vi quan sát được |
|---|---|
| Dò và chọn thiết bị | Dùng ADB để lấy serial/trạng thái, theo dõi thiết bị online và chọn target cho action. |
| Phân loại ROM/thiết bị | Chọn adapter change khác nhau tùy loại ROM, layout system và khả năng TWRP. |
| Điều khiển nguồn | Reboot Android, reboot recovery, gửi keyevent Power và bật/tắt màn hình. |
| Remote screen | Mở viewer dựa trên scrcpy cho Android 10/11. |

### 2.2. Identity, SIM và vị trí

| Chức năng | Hành vi quan sát được |
|---|---|
| Random Device Info | Tạo/lọc profile theo brand, model, OS, country và carrier. |
| Change Device | Ghi build/device identity, Android ID, serial, IMEI, SIM, MAC, location/timezone và reboot. |
| Change SIM only | Random/lọc MCC, MNC, carrier, IMSI, ICCID, phone number; có tùy chọn wipe. |
| Change Location only | Ghi latitude/longitude độc lập; có tùy chọn wipe. |
| Fake location | Có action riêng và có thể được ghép vào flow Change. |
| Timezone | Đặt timezone bằng Android service/settings và có thể ghi vào vùng identity của ROM. |

### 2.3. Xóa dữ liệu và package

| Chức năng | Hành vi quan sát được |
|---|---|
| Clear package chuẩn | `am force-stop` + `pm clear`; giữ APK nhưng reset data package. |
| Wipe package trực tiếp | `rm -rf` nhiều path dưới `/data/data`, `/data/user*`, external data và ART profile. |
| Google clean | Xóa Play Store, GMS/GSF/Gmail và account state theo cấu hình. |
| Deep clean | Xóa diện rộng trong `/data/system*`, `/data/misc*`, log/cache/stats/keystore/APEX state. |
| Factory reset branch | Nhánh riêng có thể gỡ app người dùng và xóa toàn bộ data theo option API. |
| Package manager | List, clear/wipe, uninstall package và cài APK trước/sau các workflow. |

### 2.4. Backup và restore

| Chức năng | Hành vi quan sát được |
|---|---|
| Backup thường | Đóng gói identity/package data bằng `tar` hoặc `toybox tar`. |
| Restore thường | Deep clean, giải nén backup, `restorecon`, khôi phục location và reboot. |
| Full backup/TWRP | Mount partition, backup/restore `/data` qua TWRP, wipe cache/dalvik và phục hồi file identity. |

### 2.5. Network, Google và tiện ích

| Chức năng | Hành vi quan sát được |
|---|---|
| HTTP proxy | Set/clear `settings global http_proxy`. |
| SOCKS5 | Dùng `redsocks` và `iptables`; hỗ trợ credential và clear/set route. |
| Wi-Fi | Enable/disable, đổi setting `wifi_on`, lưu SSID/MAC tùy cấu hình. |
| Google tools | Login Gmail, enable/disable/clear Play Store và Play Services, đọc account bằng `dumpsys account`. |
| APK workflow | Cài một/nhiều APK, có thư mục APK chạy trước Change hoặc sau Restore. |
| Local HTTP API | Expose route change, change SIM/location, backup/restore, proxy/SOCKS, carrier và health test. |

### 2.6. Mức độ xác nhận

- **Xác nhận trực tiếp:** command ADB/shell, path được đọc/ghi/xóa, route API và call graph gọi tới helper.
- **Xác nhận theo flow:** thứ tự Change, hai nhánh Restore, deep-clean helper, Google/package cleanup và reboot/wait.
- **Suy luận:** tên chức năng của `FUN_...`, mapping chính xác giữa một số control UI với helper khi NativeAOT đã loại symbol C#.

## 3. Thành phần chính

| Thành phần | Vai trò |
|---|---|
| `Resources\adb.exe` | Chạy ADB, push/pull, shell và theo dõi trạng thái thiết bị. |
| Các helper `ChangeDeviceInfo`, `RandomInfo`, `ChangeSim`, `ChangeLocation` | Chuẩn bị identity mới và điều phối flow change. |
| `WipePackagesBeforeChangeInfo`, `WipePackagesInTWRPAfterChangeInfo`, `wipePackagesChanger` | Các nhánh clean thường/deep/TWRP. |
| `cleanGMSPackagesAndAccounts`, `cleanGoogleAppWhenRestore` | Xóa dữ liệu/tài khoản Google theo cấu hình. |
| Thư mục `mi_info` trên system | Kênh cấu hình identity cho ROM/module tùy biến. |
| `settings_global.xml`, `settings_secure.xml` | Ghi `device_name`, `android_id`, Wi-Fi và lockscreen. |
| `toybox`, `tar`, TWRP | Backup/restore thường và full backup. |
| `redsocks`, `iptables` | SOCKS5 và redirect traffic. |
| `viewscreen10`, `viewscreen11` | Remote screen bằng scrcpy. |
| API local | Điều khiển change/backup/restore/proxy/SOCKS qua HTTP localhost. |

## 4. Flow Change Device đã xác nhận

Nhánh Change chính nằm tại `FUN_140159ba0` (`0x140159ba0`). Đây là flow khác với hai nhánh Restore ở `FUN_1401a5fe0` và `FUN_1401a69c0`.

### 4.1. Trình tự tổng quát

1. Xác thực device/config/license và lấy serial ADB đã chọn.
2. Chuẩn bị `DeviceInfo` ngẫu nhiên hoặc theo filter brand/model/OS/country/carrier; sao chép các lựa chọn hiện tại vào model chạy.
3. Chạy các bước trước change theo setting: Wi-Fi, package cần cài/gỡ/wipe, Play Store/Google và kiểm tra loại ROM/TWRP.
4. Chọn adapter ghi identity theo loại thiết bị/ROM. Binary có nhiều helper riêng (`FUN_1401a37e0`, `FUN_1401a3d20`, `FUN_1401a45c0`, `FUN_1401a39c0`, `FUN_1401a3b80`, `FUN_1401a42e0`).
5. Nếu không phải nhánh factory-reset, chạy clean thường hoặc deep clean bằng `FUN_1401a0740`/`FUN_1401a1820`, rồi xóa thêm package được cấu hình.
6. Sửa Android settings bằng `FUN_1401ab000`:
   - pull `/data/system/users/0/settings_global.xml`;
   - sửa `wifi_on`, `device_name`, `defaultValue` và `package="android"`;
   - push trả lại file;
   - pull `/data/system/users/0/settings_secure.xml`;
   - sửa `android_id`, `lockscreen_disabled`, `defaultValue` và `package="android"`;
   - push trả lại file.
7. Ghi identity vào một trong các thư mục ROM hỗ trợ:
   - `/system/etc/mi_info`
   - `/system/system/etc/mi_info`
   - `/system_root/system/etc/mi_info`
8. Chạy `restorecon -Rv` cho vùng hệ thống/dữ liệu liên quan.
9. Gửi `reboot`, chờ thiết bị ADB online lại với timeout.
10. Cài APK hậu change nếu đã cấu hình, khôi phục trạng thái Play Store, cập nhật thông tin thiết bị trên UI và kết thúc action.

Pseudocode rút gọn:

```text
ChangeDevice(serial, options):
    validate device/config/license
    newInfo = random/filter device + SIM + location
    prepare wifi/packages/google
    write identity for current ROM/device type

    if not factoryReset:
        if selected clean mode A:
            DeepCleanupA(serial, selectedPackages)
        else:
            DeepCleanupB(serial, selectedPackages)
        CleanupConfiguredPackages(serial)

    patch settings_global.xml(device_name, wifi_on)
    patch settings_secure.xml(android_id, lockscreen_disabled)
    write mi_info files
    restorecon data/system and provider paths
    adb reboot
    wait until device online
    install configured APKs
    restore Play Store state and refresh UI
```

### 4.2. Dữ liệu identity được ghi

Các lệnh `printf` cho thấy tool có thể ghi các trường sau vào `mi_info`: `android_id`, `brand`, `manufacturer`, `model`, `product`, `hardware`, `board`, `platform`, `build_id`, `display_id`, `incremental`, `release`, `sdk`, `security_patch`, `fingerprint`, `description`, `build_flavor`, `build_host`, `bootloader`, `baseband`, `serial_number`, `imei0`, `imei1`, `meid`, `imsi`, `iccid`, `phone_number`, `network_country`, `network_name`, `network_numeric`, `timezone`, `latitude`, `longitude`, `ssid`, `bssid`, `mac_address`, `wifi_mac_address`, GPU vendor/renderer và các biến test theo ROM.

Mẫu lệnh:

```sh
shell "mkdir /system/etc/mi_info"
shell "mkdir /system/system/etc/mi_info"
shell "mkdir /system_root/system/etc/mi_info"
shell "printf '{0}' > {1}/android_id"
shell "printf '{0}' > {1}/brand"
shell "printf '{0}' > {1}/model"
shell "printf '{0}' > {1}/fingerprint"
shell "printf '{0}' > {1}/imei0"
shell "printf '{0}' > {1}/imei1"
shell "printf '{0}' > {1}/imsi"
shell "printf '{0}' > {1}/iccid"
shell "printf '{0}' > {1}/timezone"
shell "printf '{0}' > {1}/wifi_mac_address"
```

## 5. Clear Data/Wipe hoạt động thế nào

MiChangerPlus có nhiều mức xóa, không phải một lệnh duy nhất.

### 5.1. Clear package chuẩn

Helper `ClearPackage`/`ClearPackages` dùng Package Manager:

```sh
shell pm clear {package}
shell "am force-stop {package}"
```

Ảnh hưởng: xóa data/cache/shared preferences/database của đúng package, giữ APK đã cài. Đây là mức nên dùng mặc định.

### 5.2. Xóa trực tiếp package

Các helper `WipePackage`, `FUN_1401a2fe0`, `FUN_1401a7980` xóa trực tiếp:

```sh
shell "rm -rf /data/data/{package}"
shell "rm -rf /data/user/0/{package}"
shell "rm -rf /data/user_de/0/{package}"
shell "rm -rf /sdcard/Android/data/{package}"
shell "rm -rf /data/misc/profiles/cur/0/{package}"
shell "rm -rf /data/misc/profiles/ref/{package}"
```

Một số nhánh còn xóa `/data/media/0/Android/data/{package}` và profile ref theo biến thể ROM. Cách này cần root, bỏ qua lifecycle của Package Manager và có thể để lại UID/SELinux state không đồng bộ.

### 5.3. Google clean

Các package xuất hiện trực tiếp trong flow:

```text
com.android.vending
com.google.android.gm
com.google.android.gms
com.google.android.gsf
com.google.android.gsf.login
```

Tool xóa data ở `data/data`, `user/0`, `user_de/0`, external Android data và ART profiles. Hậu quả dự kiến: đăng xuất Google, Play Store/Play Services tạo lại state, mất token/push state, ứng dụng phụ thuộc GMS phải đăng ký lại.

### 5.4. Deep clean thực sự được thực thi

`FUN_1401a0740` và `FUN_1401a1820` không phải chuỗi chết: các hàm này được gọi trực tiếp từ Change, Change SIM, package wipe và Restore. Chúng ghép `rm -rf` với một danh sách rất rộng rồi gửi qua ADB shell.

Các lệnh quan trọng đã xác nhận:

```sh
shell "rm -rf /data/system_de/0/* /data/system_ce/0/*"
shell "find /data/system_de/0/* | grep -v 'spblob' | xargs rm -rf"
shell "rm -rf /data/system/users/0/runtime-permissions* /data/system/users/0/wallpaper* /data/data/com.android.vending/cache/* /data/data/com.android.vending/code_cache/*"

shell "rm -rf /data/system/graphicsstats/*"
shell "rm -rf /data/system/dropbox/*"
shell "rm -rf /data/system/netstats/*"
shell "rm -rf /data/system/procstats/*"
shell "rm -rf /data/system/usagestats/*"
shell "rm -rf /data/system/syncmanager-log/*"

shell "rm -rf /data/system/users/0/settings_ssaid.xml"
shell "rm -rf /data/misc/keystore/user_0/*"
```

Danh sách deep clean còn bao gồm:

- `/data/system`: `appops*`, battery history/stats, `blobstore*`, `cachequota.xml`, device owner/policies, display manager, dropbox, graphicsstats, IFW, install sessions, integrity rules, jobs, netpolicy/netstats, notification policy/log, overlays, package cache và có nhánh chứa `packages*`, process exit/procstats, recoverable keystore, sensor privacy/service, shortcuts, sync, watchlist, user config và wallpaper.
- `/data/system_ce/0`: accounts, launch params, recents.
- `/data/system_de/0`: accounts, snapshots, persisted task IDs.
- `/data/misc`: keystore/keychain/credstore, Bluetooth/bluedroid, media, profiles/profman, recovery, stats, network watchlist, trace/logd, installd và nhiều state dịch vụ.
- `/data/misc/apexdata/com.android.*`: adb/adbd, appsearch, art/runtime, conscrypt, extservices, media, permission, resolver, sdkext, tethering và các APEX khác.
- `/data/local/*`, `/data/drm/*`, `/data/anr/*`, `/data/tombstones/*`.

### 5.5. Ảnh hưởng theo nhóm thư mục

| Nhóm | Ảnh hưởng | Khuyến nghị |
|---|---|---|
| Dữ liệu một package | Logout/reset app; mất dữ liệu cục bộ. | Cho phép theo allowlist và ưu tiên `pm clear`. |
| Google packages | Mất account/token/Play state; GMS đăng ký lại. | Chỉ bật khi người dùng chọn rõ. |
| ART profiles | App biên dịch/tối ưu lại, lần mở đầu chậm. | Có thể xóa theo package. |
| Logs/stats (`dropbox`, `graphicsstats`, `procstats`, `usagestats`) | Mất lịch sử chẩn đoán; thường được tạo lại. | Chấp nhận được trong chế độ clean nâng cao. |
| `settings_ssaid.xml` | Thay đổi SSAID/Android ID theo app, có thể phá license hoặc identity app. | Chỉ thay có kiểm soát; không xóa file mù. |
| `system_ce/system_de` accounts | Xóa tài khoản và state người dùng; service có thể lỗi tới khi reboot. | Không dùng trong clean mặc định. |
| Keystore/keychain/credstore | Mất khóa không phục hồi, token, credential và dữ liệu mã hóa của app. | Không xóa mặc định. |
| `packages*`, user list/restrictions | Nguy cơ sai UID, PackageManager lỗi, app biến mất hoặc boot lỗi. | Tuyệt đối không đưa vào flow mặc định. |
| APEX data | Có thể làm hỏng dịch vụ hệ thống/module Android, boot hoặc mạng. | Không nên xóa trong cleanup mặc định. |
| Bluetooth/Wi-Fi state | Mất pairing/cấu hình mạng. | Chỉ xóa theo action riêng có cảnh báo. |
| Device policy/owner | Có thể làm hỏng trạng thái quản trị thiết bị. | Không xóa. |

## 6. Backup và Restore

### 6.1. Backup thường

- Backup các package được chọn và thông tin identity.
- Tạo tar ở `/data/backup/{name}.tar.gz` hoặc `/sdcard` tùy flow.
- Dùng `tar` hoặc `toybox tar`.

```sh
shell mkdir /data/backup
shell tar -zcvf /data/backup/{name}.tar.gz {paths}
shell toybox tar -zcvf /data/backup/{name}.tar.gz {paths}
```

### 6.2. Full backup/TWRP

```sh
shell twrp mount /data
shell twrp mount /system
shell twrp mount /vendor
shell twrp backup D BackupFull
shell twrp restore /sdcard/BackupFull
shell twrp wipe cache
shell twrp wipe dalvik
```

Nhánh Restore thường `FUN_1401a5fe0`:

1. Xóa `system_de/0` và `system_ce/0`.
2. Push `/sbin/toybox`, `chmod 777`.
3. Chạy deep cleanup.
4. Push và giải nén `/data/backup/{file}`.
5. Ghi lại latitude/longitude nếu có.
6. `restorecon -Rv` các path phục hồi.
7. Xóa runtime permissions/wallpaper/Play Store cache và file tar tạm.

Nhánh TWRP `FUN_1401a69c0` làm tương tự nhưng dùng `/sdcard/info.tar.gz`, `twrp restore /sdcard/BackupFull`, sau đó copy `info` về vị trí đích và sửa SELinux context.

## 7. Chi tiết action khác

| Action | Cách thực hiện/ghi chú |
|---|---|
| Change SIM only | Random/filter MCC/MNC/carrier, ghi SIM identity; `wipe=true/false` quyết định có clean app. |
| Change location only | Ghi latitude/longitude; có tùy chọn wipe. |
| Package manager | List package, wipe bằng `pm clear` hoặc xóa trực tiếp, uninstall bằng `pm uninstall`. |
| Install APK | `InstallApk`/`InstallPackages`; có folder APK riêng trước Change và trước Restore. |
| Google tools | Login Gmail, clear/enable/disable Play Store và Play Services, đọc account qua `dumpsys account`. |
| HTTP proxy | `settings put global http_proxy {host:port}` và đọc lại bằng `dumpsys wifi`. |
| SOCKS5 | Dùng `redsocks` + `iptables`, có clear/set và tùy chọn tắt Wi-Fi khi đổi SOCKS. |
| Wi-Fi | `svc wifi enable/disable`, `settings put global wifi_on`, nhớ SSID/MAC tùy cấu hình. |
| Timezone | `service call alarm 3 s16 {timezone}` và `settings put global auto_time_zone`. |
| Remote screen | scrcpy 10/11 đi kèm tool. |
| Reboot/recovery/screen | `reboot`, reboot recovery, keyevent power, bật/tắt màn hình. |
| Fake location | Fake location riêng hoặc đi cùng Change. |

## 8. API local

Tài liệu gốc nằm ở `G:\MiChangerPlus\Resources\MiChangerPlus_API.txt`. Các route chính:

```text
GET /change?serial=...&filter_brand=...&filter_model=...&filter_os=...&filter_country=...&filter_carrier=...&custom_carrier=...&lat=...&long=...&factory_reset=...
GET /changesimonly?serial=...&filter_country=...&filter_carrier=...&custom_carrier=...&wipe=...
GET /changelocationonly?serial=...&lat=...&long=...&wipe=...
GET /backuponly?serial=...&note=...&filename=...&full=...
GET /backup?serial=...&note=...&filename=...&factory_reset=...&full=...
GET /restore?serial=...&filename=...&gmail=...&lat=...&long=...
GET /getlist?type=all|PHONE_TYPE
GET /clearproxy?serial=...
GET /setproxy?serial=...&proxy=HOST:PORT
GET /clearsocks?serial=...
GET /setsocks?serial=...&socks=HOST:PORT[:USER:PASSWORD[:IP_SOCKS]]
GET /getcarrier?country=...
GET /test
```

`factory_reset=true` được tài liệu tool mô tả là gỡ app người dùng và xóa toàn bộ dữ liệu điện thoại; đây là action khác đáng kể so với clean package.

## 9. Đối chiếu với DeepDroidChanger hiện tại

DeepDroidChanger không sao chép nguyên cách gọi từng command của MiChangerPlus. Flow mới gom trách nhiệm vào `DeviceChangeService`, `DeviceDataCleanupService`, `DevicePackageService`, `AdbCommandService` và các dialog/ViewModel tương ứng.

Các điểm đã tối ưu:

1. **Guard và xác nhận:** nút Change chỉ chạy khi thiết bị `Online` và đã có profile; dialog cảnh báo xuất hiện trước khi kiểm tra root và xóa dữ liệu.
2. **Package cleanup an toàn hơn:** Default dùng `pm clear`, chỉ xử lý package thực sự được cài, loại `com.android.shell`; `rm -rf` là lựa chọn Advanced và chỉ nhắm tám path theo package.
3. **Giữ directory root:** account/residual cleanup dùng `find ... -not -type d -delete` thay vì xóa toàn bộ path. Cách này vẫn xóa file thông tin nhưng giữ cây thư mục mà Android/ROM có thể không tạo lại đúng owner/mode/SELinux context.
4. **Bảo vệ registry, persistent property, Bluetooth và Wi-Fi:** không xóa `/data/app`, `/data/mi_info`, `/data/property/persistent_properties`, `/data/misc/bluetooth`, `/data/misc/bluedroid`, file bắt đầu bằng `/data/system/package*` hoặc `/data/misc/apexdata/com.android.wifi`.
5. **Ít ADB round-trip:** package cleanup, account cleanup và residual cleanup được hợp nhất thành script truyền qua stdin của `adb shell sh`; không đưa script dài vào Windows command line và không push file tạm lên máy.
6. **Fail-fast:** command bắt buộc dùng `|| exit $?`; service không áp profile/reboot như thể thành công nếu cleanup trả exit code khác `0`.
7. **Identity có contract tập trung:** property được khai báo trong `DeviceSpoofPropertyConstants`, áp dụng qua `IAdbCommandService`, sau reboot có bước kiểm tra Android ID.

Các rủi ro còn phải coi là chủ ý thiết kế, không phải cleanup thông thường:

- Default vẫn xóa toàn bộ package data, Google account state và file trong `system_ce/system_de`; không có backup/rollback tự động.
- Residual list vẫn rộng, gồm keystore/credstore, Bluetooth, vendor state và nhiều APEX directory. Giữ directory root giảm rủi ro path không tái tạo được nhưng không đảm bảo service Android sẽ phục hồi mọi file đã mất.
- `settings_ssaid.xml` vẫn nằm trong danh sách residual; ứng dụng có thể thay đổi identity theo app và làm mất state/license cũ.
- Root check chỉ chứng minh `adbd` chạy UID 0; tính tương thích của ROM/module với các property spoof vẫn phải test riêng.

Vì vậy Change Device phải được xem là workflow wipe có chủ đích trên thiết bị đã backup, không phải action “clear cache”.

# Lx 手机遥控（C1）

这是 C1 的第一阶段：**Windows 启动器提供局域网遥控服务，手机浏览器/APK 连接电脑**。

不是安卓本地运行 Minecraft；游戏仍然在 Windows 电脑上启动。

## 使用

1. 打开 Windows 启动器
2. 设置 → 手机遥控…
3. 点“启动服务”
4. 复制“手机访问地址”
5. 手机和电脑连同一个 Wi-Fi/局域网
6. 手机浏览器打开地址，或打开 APK 粘贴地址

地址示例：

```text
http://192.168.10.25:47821/?token=xxxxxxxxxxxxxxxx
```

首次监听时 Windows 防火墙会弹窗，选择“允许访问”。

## API

所有 `/api/*` 请求需要：

```http
X-Lx-Token: <token>
```

| 方法 | 路径 | 作用 |
|---|---|---|
| GET | /api/ping | 版本/连通性 |
| GET | /api/snapshot | 状态、实例列表 |
| GET | /api/logs?lines=200 | 最近日志 |
| POST | /api/launch | 启动实例，body `{"name":"实例名"}` |

页面本身不需要 token；token 只保护 API。页面地址里带的 token 会存到手机浏览器 localStorage。

## 安全边界

- 只用于局域网，不要把端口转发到公网
- 使用随机 16 位 hex token
- HTTP 明文；不要在公共 Wi-Fi 上开放
- 服务只在“手机遥控”窗口打开期间运行
- 手机端只负责请求启动/读取状态，不接触账号密码

## APK

构建：

```powershell
.\build-android.ps1
```

产物：

```text
dist\Lx-yulin-carp-remote-1.4.0.apk
```

包含：

- `arm64-v8a`
- `armeabi-v7a`

这是 WebView 外壳，加载电脑上的移动端页面。当前是调试签名，适合自己安装测试；上架/分发前需要正式 keystore。

安装：

1. 把 APK 发到手机
2. 允许安装未知来源
3. 打开后粘贴电脑“手机访问地址”
4. 连接

## 限制

- 电脑必须开机且启动器运行
- 手机和电脑必须能互相访问
- 服务窗口关闭后服务停止
- 不是安卓本地 Java 版启动器；那属于 C2

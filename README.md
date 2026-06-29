# TimeLab

TimeLab 是一个简单的本地时间管理工具，用于管理任务、进行番茄钟计时，并记录专注历史。

当前项目仍以 MVP 为目标：保持功能清晰、结构简单，不引入复杂任务管理、云同步或数据库。

---

## 技术栈

* C#
* WPF
* MVVM
* JSON 本地存储
* xUnit 测试

---

## 功能

### Todo

* 创建任务
* 为任务设置计划时长
* 标记任务完成
* 删除任务
* 将任务关联到当前计时

### Timer

* 开始 / 暂停 / 停止 / 清除计时
* 手动正计时
* 预设时长倒计时
* 专注 / 短休 / 长休模式
* 循环番茄模式，可设置专注时长、休息时长和轮数
* 到时提醒和提示音

### Session Log

* 计时停止后自动记录专注 Session
* 保存开始时间、结束时间、时长、模式和关联任务
* 删除历史记录
* 显示今日专注次数、专注分钟数和完成任务数

### UI 体验

* 现代化 WPF 界面
* 浅色 / 深色模式切换，并保存设置
* 系统托盘显示和提醒
* 快捷键：
  * Space：开始 / 暂停
  * Esc：停止 / 清除

---

## 运行方式

1. 打开 `TimeLab.slnx`
2. 运行 `TimeLab.App`

也可以在项目根目录执行：

```powershell
dotnet build TimeLab.slnx
```

---

## 测试

项目包含 `TimeLab.Tests` 测试项目，使用 xUnit 覆盖核心服务和 JSON 存储逻辑。

运行全部测试：

```powershell
dotnet test TimeLab.slnx
```

---

## 项目结构

* `TimeLab.App`：UI 层，包含 WPF View、ViewModel、Converter、主题和托盘交互
* `TimeLab.Application`：应用服务层，包含任务服务和番茄钟服务
* `TimeLab.Core`：领域模型层，包含任务、Session、计时器状态和模式
* `TimeLab.Infrastructure`：数据存储层，使用 JSON 文件保存任务和专注记录
* `TimeLab.Tests`：测试项目，验证 Application 服务和 Infrastructure 存储行为

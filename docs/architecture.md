# Architecture

## Layers

* `TimeLab.App`：UI 层，WPF + ViewModel
* `TimeLab.Application`：应用服务层
* `TimeLab.Core`：领域模型层
* `TimeLab.Infrastructure`：数据存储层

---

## Dependency Rules

当前依赖方向：

* App → Application, Infrastructure
* Application → Core
* Infrastructure → Application, Core
* Core → 无依赖

这些依赖方向符合当前项目规则。Core 不依赖任何其他层。

---

## Responsibilities

### Core

定义领域模型和基础状态：

* `TaskItem`：任务
* `PomodoroSession`：一次专注或休息记录
* `TimerState`：计时器状态
* `TimerStatus`：空闲、运行、暂停、停止
* `FocusMode`：专注、短休、长休

### Application

处理应用层业务逻辑：

* `TaskService`：创建任务、获取任务、完成任务、删除任务
* `PomodoroService`：计时状态、专注模式、循环模式、Session 生成
* `ITaskRepository`：任务仓储接口
* `ISessionRepository`：Session 仓储接口

Application 只依赖 Core，不关心具体 JSON 文件路径或 UI 展示。

### Infrastructure

实现本地数据持久化：

* `JsonTaskRepository`：使用 JSON 文件保存任务
* `JsonSessionRepository`：使用 JSON 文件保存 Session
* 数据目录位于用户本地应用数据目录下的 `TimeLab`
* JSON 文件损坏时会备份损坏文件并返回空列表

Infrastructure 依赖 Application 的仓储接口和 Core 的模型。

### App

负责 WPF 展示和用户交互：

* `MainWindow`：主窗口、系统托盘、快捷键、主题切换和设置保存
* `MainViewModel`：Todo、Timer、Session Log 的绑定状态和命令
* Converter：时长显示、秒数显示、任务标题显示
* XAML 样式：任务勾选框、开关、主题资源

View 中不应编写核心业务规则，业务行为应优先放在 Application 服务中。

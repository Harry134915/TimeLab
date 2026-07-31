# Architecture

## Layers

* `TimeLab.App`：UI 层，WPF + ViewModel
* `TimeLab.Application`：应用服务层
* `TimeLab.Core`：领域模型层
* `TimeLab.Infrastructure`：数据存储层
* `TimeLab.Tests`：测试项目

---

## Dependency Rules

当前依赖方向：

* App → Application, Infrastructure
* Application → Core
* Infrastructure → Application, Core
* Tests → App, Application, Core, Infrastructure
* Core → 无依赖

生产代码的依赖方向符合当前项目规则。Core 不依赖任何其他层。
Tests 引用 App 是为了验证 ViewModel、命令和 Converter，不会启动真实 WPF 窗口。

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
* `JsonFileStore`：统一处理 JSON 读取、原子临时文件写入、崩溃恢复、备份和文件级并发保护
* 数据目录位于用户本地应用数据目录下的 `TimeLab`
* JSON 文件损坏时会备份损坏文件并返回空列表
* 同一进程内使用信号量串行读写；Windows 独立进程之间使用命名互斥体保护同一文件
* 进程在临时文件完成后异常退出时，下次读取会恢复较新的有效临时文件
* 锁持有进程异常终止后，下一次操作会接管废弃互斥体并继续写入

Infrastructure 依赖 Application 的仓储接口和 Core 的模型。

### App

负责 WPF 展示和用户交互：

* `MainWindow`：窗口装配、响应式滚动、快捷键和具体控件的状态播报路由
* `AppComposition`：集中创建 Repository、Service、共享交互状态、三个功能 ViewModel 和根 ViewModel
* `TrayIconService`：系统托盘图标、托盘菜单和气泡提醒
* `WindowLifecycleCoordinator`：系统托盘显示、窗口隐藏、退出防重入和活动计时退出协调
* `WindowDialogService`：删除确认、活动计时退出选择和保存失败提示
* `ThemeManager`：深浅色资源、切换动画、高对比度适配和主题设置持久化
* `LiveRegionAnnouncer`：统一发布 Windows UI Automation Live Region 事件
* `MainViewModel`：根协调器，负责主题、通知、分模块启动加载和退出流程
* `TaskListViewModel`：任务集合、输入校验、任务选择、任务命令和任务写入队列
* `TimerViewModel`：计时区域的公开绑定、循环输入和 WPF 命令
* `TimerWorkflow`：计时操作串行化、Tick 刷新、启动 / 暂停 / 停止 / 退出工作流
* `TimerTargetCoordinator`：目标到达后的 Session 保存、模式推进和失败重试
* `TimerPresentationState`：计时展示状态与时间、目标文案生成
* `SessionLogViewModel`：专注记录、记录删除、写入队列和今日统计
* `WorkspaceInteractionState`：发布任务写入、计时操作、Session 写入、活动计时和退出准备状态，统一刷新互斥命令
* Converter：时长显示、秒数显示、任务标题显示
* XAML 样式：任务勾选框、开关、主题资源

功能 ViewModel 的依赖保持单向：计时工作流读取 `TaskListViewModel` 的当前任务，并将生成的记录交给 `SessionLogViewModel`。根 ViewModel 不保留功能属性或命令的转发 API。

View 中不应编写核心业务规则，业务行为应优先放在 Application 服务中。

### Tests

验证核心服务和存储行为：

* `TaskServiceTests`：验证任务创建、完成、删除和保存失败恢复
* `PomodoroServiceTests`：验证计时开始、暂停、继续和 Session 生成
* `JsonTaskRepositoryTests`、`JsonSessionRepositoryTests`：验证 JSON 读写、损坏备份和并发操作
* `MainViewModelTimerTests`、`MainViewModelInteractionTests`：验证计时状态、核心专注流程和命令状态
* `TaskMutationGateTests`、`TimerPersistenceRecoveryTests`：验证异步操作串行化、退出等待和保存失败重试
* `ViewModelCoordinationTests`：验证分模块加载、共享命令互斥和退出失败后的命令恢复
* Converter 与命令测试：验证显示转换和异步命令行为
* 测试使用临时目录，不写入真实用户数据目录

Tests 可以依赖全部项目，但 App 层测试应以 ViewModel、命令和 Converter 为主，不启动真实 WPF 窗口。

# Workflow

## Step 1: Scaffold

* 创建项目结构
* 创建解决方案和项目
* 配置依赖关系

状态：已完成。

---

## Step 2: Core

* 实现 Task 模型
* 实现 Session 模型
* 实现 Timer 状态
* 实现专注 / 短休 / 长休模式

状态：已完成。

---

## Step 3: Application

* 实现 `TaskService`
* 实现 `PomodoroService`
* 定义任务仓储接口
* 定义 Session 仓储接口

状态：已完成。

---

## Step 4: Infrastructure

* 使用 JSON 实现任务数据存储
* 使用 JSON 实现 Session 数据存储
* 处理 JSON 文件不存在和损坏的情况

状态：已完成。

---

## Step 5: UI

* 实现 WPF 界面
* 绑定 ViewModel
* 展示 Todo、Timer、Session Log
* 增加深色模式、系统托盘、快捷键、到时提醒
* 增加预设时长和循环番茄等体验增强

状态：已完成。

---

## Step 6: Tests

* 新增 `TimeLab.Tests` 测试项目
* 使用 xUnit 编写自动化测试
* 覆盖 `TaskService` 基础行为
* 覆盖 `PomodoroService` 基础计时行为
* 覆盖 JSON 任务仓储的读取、写入和损坏备份

状态：已开始。

---

## 当前说明

项目已经按原始 MVP 顺序完成到 Step 5，并在 UI 阶段加入了轻量体验增强。

后续工作仍应保持小步推进：

1. 先保持文档与当前实现一致
2. 再修正明确的计时逻辑问题
3. 然后做小范围代码质量优化
4. 持续补充 Application / Infrastructure 层测试
5. 最后再考虑 AI Insight 或复杂统计等新功能

---

## Rule

必须按顺序完成，不允许跳步骤。

后续新增功能前，应先确认当前阶段目标，避免在没有文档和边界说明的情况下继续扩大范围。

默认不引入新的第三方库或框架。若测试、构建或功能实现确实需要引入，例如 xUnit、NUnit 或其他测试框架，必须先说明用途、影响范围和替代方案，并获得用户同意后再添加依赖。

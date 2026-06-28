# AGENTS.md

## 项目说明

你正在开发 TimeLab，一个简单的 WPF 应用，包含以下功能：

* Todo 列表
* 番茄钟计时器
* 专注记录（Session Log）

这是一个 MVP 项目，目标是保持简单和清晰，不做复杂设计。

---

## 技术栈

* 语言：C#
* UI：WPF
* 架构：MVVM
* 存储：JSON（当前不使用数据库）

---

## 架构规则

分层结构：

* TimeLab.App：UI 层（WPF + ViewModel）
* TimeLab.Application：应用服务层
* TimeLab.Core：领域模型层
* TimeLab.Infrastructure：数据存储层

依赖关系：

* App → Application, Infrastructure
* Application → Core
* Infrastructure → Application, Core
* Core → 不允许依赖任何层

禁止违反依赖方向。

---

## 限制

* 不要实现未要求的功能
* 不要引入新的框架或第三方库
* 不要修改无关代码
* 不要移动文件结构
* 不要在 View 中编写业务逻辑

---

## 编码原则

* 保持代码简单清晰
* 使用明确的命名
* 避免过度设计
* 类和方法尽量小

---

## 工作流程

每次必须按以下步骤执行：

1. 先说明将要做的内容
2. 只实现当前要求的功能
3. 不修改其他模块
4. 最后总结改动

---

## 当前目标

按顺序完成 MVP：

1. 项目脚手架
2. Core 模型
3. Application 服务
4. Infrastructure（JSON 存储）
5. UI

不允许跳步骤。

---

## 行为规则

* 如果需求不清楚，先提问
* 如果任务过大，拆分为小任务
* 优先保证正确性，而不是完整性

---

## 完成标准

任务完成必须满足：

* 能正常编译
* 无明显错误
* 符合当前需求
* 没有引入额外改动
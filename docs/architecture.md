# Architecture

## Layers

* TimeLab.App：UI 层 WPF + ViewModel (WPF 应用)
* TimeLab.Application：应用服务层
* TimeLab.Core：领域模型
* TimeLab.Infrastructure：数据存储

---

## Dependency Rules

* App → Application, Infrastructure
* Application → Core
* Infrastructure → Application, Core
* Core → 无依赖

---

## Responsibilities

### Core

* 定义领域模型（Task, Session, TimerState）

### Application

* 处理业务逻辑（TaskService, TimerService）

### Infrastructure

* 数据持久化(JSON 文件)

### App

* UI 展示
* ViewModel
* 用户交互
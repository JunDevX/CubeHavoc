name: Сообщить об ошибке (Bug)
description: Создайте отчет, чтобы помочь нам исправить баг в игре
title: "[BUG]: "
labels: ["bug"]
body:
  - type: input
    id: version
    attributes:
      label: Версия игры
      placeholder: "например: v1.0, latest release"
    validations:
      required: true

  - type: textarea
    id: description
    attributes:
      label: Описание проблемы
      placeholder: "Четкое и понятное описание того, в чем заключается баг"
    validations:
      required: true

  - type: textarea
    id: steps
    attributes:
      label: Шаги для воспроизведения
      placeholder: |
        1. Перейти в '...'
        2. Нажать на '...'
        3. Увидеть ошибку
      value: |
        1. 
        2. 
        3. 
    validations:
      required: false

  - type: textarea
    id: expected
    attributes:
      label: Ожидаемое поведение
      placeholder: "Опишите, что должно было произойти на самом деле"
    validations:
      required: false

  - type: textarea
    id: system
    attributes:
      label: Ваша система (по желанию)
      placeholder: |
        - ОС: Windows / macOS / Linux
        - Процессор / Видеокарта:
    validations:
      required: false

  - type: checkboxes
    id: checklist
    attributes:
      label: Чеклист
      options:
        - label: Я искал ответ в официальном DOCS и README!
          required: true

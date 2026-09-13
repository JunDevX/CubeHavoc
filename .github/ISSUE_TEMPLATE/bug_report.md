name: Предложить идею (Feature)
description: Предложите новую фичу или улучшение для Cube Havoc 3D
title: "[FEATURE]: "
labels: ["enhancement"]
body:
  - type: textarea
    id: feature
    attributes:
      label: Предложение
      placeholder: "Опишите вашу идею для игры"
    validations:
      required: true

  - type: textarea
    id: why
    attributes:
      label: Зачем это нужно?
      placeholder: "Какую проблему это решает или как улучшит геймплей?"
    validations:
      required: false

  - type: textarea
    id: how
    attributes:
      label: Как это должно работать?
      placeholder: "Опишите механики или интерфейс, если есть видение"
    validations:
      required: false

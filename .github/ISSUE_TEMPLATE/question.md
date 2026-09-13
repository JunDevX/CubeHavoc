name: Задать вопрос (Question)
description: Если возникли сложности с запуском, Radmin/локальной связью или сборкой
title: "[QUESTION]: "
labels: ["question"]
body:
  - type: textarea
    id: question
    attributes:
      label: Ваш вопрос
      placeholder: "Подробно опишите, с чем возникла проблема или что хотите уточнить"
    validations:
      required: true

  - type: checkboxes
    id: checklist
    attributes:
      label: Чеклист
      options:
        - label: Я предварительно прочитал README и инструкции в репозитории
          required: true

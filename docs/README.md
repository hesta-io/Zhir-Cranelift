# Introduction

ZhirAI is the first AI-focused company in Kurdistan. It's main focus is to digitize the Kurdish language by developing powerful tools like OCRs, Speech-to-Text and more. Our first product is Kurdish OCR.

## Other pages

1. [Overview of tessaract engine](./tesseracticdar2007.pdf)
2. [Training](./training.md)
3. [Create an ocr engine theoritcal approach](https://towardsdatascience.com/what-is-ocr-7d46dc419eb9)


## Tools 
1.  [QT Box-editor](https://zdenop.github.io/qt-box-editor/) for training generating box files

## Products we will work on:

### Kurdish OCR

We are using [Tesseract](https://github.com/tesseract-ocr/tesseract) for the OCR. Over the years we have collected [a lot of data](https://github.com/developerstree/kurdishresources) for the Kurdish languages. Because each language has its own model, we can documents in multiple languages. We plan to develop two models for the Kurdish langauge:

- A model for printed documents
- A model for manuscripts

Although the manuscript model is more useful, the printed documents model is easier and allows us to have a product ready to  in less time. There is already [a sample model](https://github.com/Shreeshrii/tesstrain-ckb) for the Kurdish language that has been trained using various fonts.

### Kurdish Audio transcription (Speech To Text)

We are going to use Mozilla [DeepSpeech](https://github.com/mozilla/DeepSpeech).

## Resources

1. https://docs.google.com/document/d/1xY2GsTK6f9aZ9U6DyV8Z65Y_BASmzVEU9vajJkY__4c/edit?usp=sharing
2. https://distill.pub/2017/ctc/
3. https://tesseract-ocr.github.io/tessdoc/
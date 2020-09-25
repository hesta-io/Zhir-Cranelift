# Training

This page is dedicated for the steps to train lstm models for tesseract.

1. Read the [overview](https://tesseract-ocr.github.io/tessdoc/TrainingTesseract-4.00.html) about tesseract training and setting up training environment
2. Make training data by following the [overview](https://tesseract-ocr.github.io/tessdoc/TrainingTesseract-4.00.html#creating-training-data).
3. Follow the steps in https://github.com/tesseract-ocr/tesstrain

Note: For `tesstrain` steps to work, I also had to install `image` package in pip3:

`pip3 install image`

### Install tesseract

First you have to install tesseract (including training binaries).

### Make training data

1. Go to the folder containing [tesseract repo](https://github.com/tesseract-ocr/tesseract).
2. Run:

```
src/training/tesstrain.sh --fonts_dir /mnt/f/ZhirAI/data/fonts/kur_ara --lang kur_ara --linedata_only --noextract_font_properties --langdata_dir /mnt/f/ZhirAI/data/langdata/ --tessdata_dir /mnt/f/ZhirAI/data/tessdata/ --output_dir /mnt/f/ZhirAI/data/kur_aratrain/
```

### Create model

```
lstmtraining --debug_interval 0 \
                                      --traineddata /mnt/f/ZhirAI/data/kur_aratrain/kur_ara/kur_ara.traineddata \
                                      --net_spec '[1,36,0,1 Ct3,3,16 Mp3,3 Lfys48 Lfx96 Lrx96 Lfx256 O1c89]' \
                                      --model_output /mnt/f/ZhirAI/data/kur_araoutput/base --learning_rate 20e-4 \
                                      --train_listfile /mnt/f/ZhirAI/data/kur_aratrain/kur_ara.training_files.txt \
                                      --eval_listfile /mnt/f/ZhirAI/data/kur_aratrain/kur_ara.training_files.txt \
                                      --max_iterations 5000
```

Note: in `O1c89`, the `89` should be replaced with the number of entries in `.unicharset` file.



### Tips

- When creating a new language model, start with a very small training text (1000 - 5000 lines), make sure everything is working correctly and only after that train a full sized model.

## Resources

1. https://tesseract-ocr.github.io/tessdoc/TrainingTesseract-4.00.html
2. https://tesseract-ocr.github.io/tessdoc/ImproveQuality.html
3. https://github.com/tesseract-ocr/tesstrain
4. https://github.com/tesseract-ocr/tesstrain/wiki
5. https://github.com/tesseract-ocr/tesstrain/wiki/Arabic-Handwriting
6. https://tesseract-ocr.github.io/tessdoc/4.0-with-LSTM.html
7. https://www.youtube.com/watch?v=TpD76k2HYm
8. https://livezingy.com/train-tesseract-lstm-with-make-on-windows-2/
9. https://github.com/Shreeshrii/tesstrain-ckbImpact
10. https://github.com/Shreeshrii/tesstrain-ckb


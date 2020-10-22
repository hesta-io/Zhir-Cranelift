from skimage import io
from skimage import color
from skimage import viewer
from skimage import filters
from skimage import transform
from functions import deskew
from pathlib import Path
import plac

@plac.opt('output_file', "Optional output file", type=Path)
@plac.opt('input_file', "Input file", type=Path)
def main(input_file, output_file='.'):
    # imgPath = "./images/5-rotated.jpg"
    imgPath = input_file
    img = io.imread(imgPath, as_gray=True)
    # binarize input image and apply local theresould
    adaptiveThresh = filters.thresholding.threshold_sauvola(img,window_size=71 )
    binarizedImage = img >= adaptiveThresh

    # Fixing document skew
    rotationAngle = deskew(binarizedImage)
    fixedImage = transform.rotate(binarizedImage, rotationAngle, cval=1, mode="constant")
    finalImage =  fixedImage * 255
    io.imsave(output_file,finalImage)

if __name__ == '__main__':
    plac.call(main)

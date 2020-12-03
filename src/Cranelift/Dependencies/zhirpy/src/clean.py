# - https://scikit-image.org/docs/dev/
# - argparse: https://docs.python.org/3/library/argparse.html

from skimage.filters import gaussian, threshold_otsu
from skimage.feature import canny
from skimage.transform import probabilistic_hough_line, rotate
from skimage import io
from skimage import filters
from skimage import transform
from skimage import util
from skimage import exposure
import argparse
import numpy as np
import shutil
import os


def deskew(image):
    # threshold to get rid of extraneous noise
    thresh = threshold_otsu(image)
    normalize = image > thresh
    # gaussian blur
    blur = gaussian(normalize, 3)
    # canny edges in scikit-image
    edges = canny(blur)
    # hough lines
    hough_lines = probabilistic_hough_line(edges)
    # hough lines returns a list of points, in the form ((x1, y1), (x2, y2))
    # representing line segments. the first step is to calculate the slopes of
    # these lines from their paired point values
    slopes = [
        (y2 - y1) / (x2 - x1) if (x2 - x1) else 0 for (x1, y1), (x2, y2) in hough_lines
    ]
    # it just so happens that this slope is also y where y = tan(theta), the angle
    # in a circle by which the line is offset
    rad_angles = [np.arctan(x) for x in slopes]
    # and we change to degrees for the rotation
    deg_angles = [np.degrees(x) for x in rad_angles]
    # which of these degree values is most common?
    histo = np.histogram(deg_angles, bins=180)
    # correcting for 'sideways' alignments
    rotation_number = histo[1][np.argmax(histo[0])]

    if rotation_number > 45:
        rotation_number = -(90 - rotation_number)
    elif rotation_number < -45:
        rotation_number = 90 - abs(rotation_number)
    return rotation_number


# below function will check if the input image is a screenshot or not
# by looking at histogram spikes if there exisit more than 2 spikes that are
# 1/2 of the max spike this will be considered a reguller image and should be
# pre processed what this means is that the image contains shadows and color variations
# as we convert to grayscale color variation will minimize and shadow effects will remain
# so this method is some kind of accurate and very fast to compute
# we may increase the accuracy by smoothing the histogram(1-D array) but we need actual data
# and if the current method did not help we will start the smoothing the histogram
def isScreenshot(image):
    hist = exposure.histogram(image)
    histUnit = hist[0]
    maxValue = np.max(histUnit)
    spikesFilter = histUnit >= (maxValue / 2)
    spikes = histUnit[spikesFilter]
    if len(spikes) > 1:
        return False
    else:
        return True


parser = argparse.ArgumentParser(
    description="Pre-processes an image and prepares it for OCR."
)

parser.add_argument("source", help="The path for the source image.")

parser.add_argument("dest", help="The path for the cleaned-up image.")

args = parser.parse_args()

# Read source image
img = io.imread(args.source, as_gray=True)
avg = img.mean(axis=0).mean(axis=0)

if avg < 0.5:
    # Images whith black background should NOT be cleaned
    directory = os.path.dirname(args.dest)
    if len(directory) > 0:
        os.makedirs(directory, exist_ok=True)
    shutil.copy(args.source, args.dest)

    print("DID NOTHING")
elif isScreenshot(img):
    io.imsave(args.dest, img)

    print("JUST GRAYSCALE")
else:
    # Binarize input image and apply local theresould
    adaptiveThresh = filters.thresholding.threshold_sauvola(
        img, r=0.2, window_size=11
    )  # this current method gives far better results
    # adaptiveThresh = filters.threshold_local(img, block_size = 11 , offset = 0.05, method = "mean")

    binarizedImage = img >= adaptiveThresh

    # Fix document skew
    rotationAngle = deskew(binarizedImage)
    fixedImage = transform.rotate(
        binarizedImage, rotationAngle, cval=1, mode="constant"
    )

    # Save result
    io.imsave(args.dest, fixedImage)
    print("CLEANED")

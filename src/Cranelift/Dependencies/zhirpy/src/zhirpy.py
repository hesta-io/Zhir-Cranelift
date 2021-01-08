# - https://scikit-image.org/docs/dev/
# - argparse: https://docs.python.org/3/library/argparse.html
import cv2
from skimage.filters import gaussian, threshold_otsu
from skimage.feature import canny
from skimage.transform import probabilistic_hough_line, rotate
from skimage import io
from skimage import filters
from skimage import transform
from skimage import util
from skimage import exposure
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

def removeShadows(img):
    rgb_planes = cv2.split(img)
    result_norm_planes = []
    for plane in rgb_planes:
        dilated_img = cv2.dilate(plane, np.ones((7,7), np.uint8))
        bg_img = cv2.medianBlur(dilated_img, 21)
        diff_img = 255 - cv2.absdiff(plane, bg_img)
        norm_img = cv2.normalize(diff_img,None, alpha=0, beta=255, norm_type=cv2.NORM_MINMAX, dtype=cv2.CV_8UC1)
        result_norm_planes.append(norm_img)
    shadowremov = cv2.merge(result_norm_planes)
    return shadowremov

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
    if len(spikes) > 2:
        return False
    else:
        return True

def addBorders(img):
    row, col = img.shape[:2]
    bottom = img[row-2:row, 0:col]

    bordersize = 10
    borderedImage = cv2.copyMakeBorder(
        img,
        top=bordersize,
        bottom=bordersize,
        left=bordersize,
        right=bordersize,
        borderType=cv2.BORDER_CONSTANT,
        value=0
    )
    return borderedImage

def clean(source, dest):
    
    # Read source image
    img = cv2.imread(source, 0)
    avg = img.mean(axis=0).mean(axis=0)
        
    if avg < 0.5:
        # Images whith black background should NOT be cleaned
        directory = os.path.dirname(dest)
        if len(directory) > 0:
            os.makedirs(directory, exist_ok=True)
        shutil.copy(source, dest)

        print("DID NOTHING")
    elif isScreenshot(img):
        hist = exposure.histogram(img)
        histUnit = hist[0]
        middlePoint = round(len(histUnit)/2)
        leftPart = sum(histUnit[0:middlePoint])
        rightPart = sum(histUnit[middlePoint:len(histUnit)])
        if leftPart > rightPart:
            # screenshot is taken from a dark background with white text
            # invert the image to fix them
            invertedImage = util.invert(img)
            io.imsave(dest, invertedImage)
            print("IMAGE INVERTED")
            
        else:
            io.imsave(dest, img)
            print("JUST GRAYSCALE")
            
            
    
    else:
        # remove shadows
        img = removeShadows(img)
        
        # denoise the image
        img =  cv2.fastNlMeansDenoising(img,None,10,7,21)
        
        # Binarize input image and apply local theresould
        # binarized = cv2.adaptiveThreshold(
        #     img, 255, cv2.ADAPTIVE_THRESH_MEAN_C, cv2.THRESH_BINARY, 13, 10
        # )
        # binarizedImage = binarized
        # Fix document skew
        # rotationAngle = deskew(binarizedImage)
        # fixedImage = transform.rotate(
        #     binarizedImage, rotationAngle, cval=1, mode="constant"
        # )
        # fixedImage = addBorders(fixedImage)

        # Save result
        # io.imsave(dest, fixedImage)
        io.imsave(dest, img)
        print("CLEANED")

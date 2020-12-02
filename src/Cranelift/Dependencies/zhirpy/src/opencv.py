import cv2 
from skimage.filters import gaussian, threshold_otsu
from skimage.feature import canny
from skimage.transform import probabilistic_hough_line, rotate
from skimage import transform
import numpy as np




img = cv2.imread('../images/4-rotated.jpg', 0 )

binarized = cv2.adaptiveThreshold(img,255,cv2.ADAPTIVE_THRESH_MEAN_C, cv2.THRESH_BINARY,11,10)


cv2.imshow("img",cv2.resize(img, (600, 960)))
cv2.imshow("deskewed_image",cv2.resize(binarized, (600, 960)))

# cv2.imwrite('C:/Users/N/Desktop/Test_gray.jpg', image_gray)
cv2.waitKey(0)

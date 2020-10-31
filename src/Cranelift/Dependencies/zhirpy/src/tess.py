# - argparse: https://docs.python.org/3/library/argparse.html
# - pytesseract: https://pypi.org/project/pytesseract/

try:
    from PIL import Image
except ImportError:
    import Image
import pytesseract
import argparse
import sys

parser = argparse.ArgumentParser(
    description="Runs tesseract on an image and saves the result. The result will either be in PDF format or Plain text format based on the destination extension.")

parser.add_argument(
    "source", help="The path for the source image.")

parser.add_argument(
    "dest", help="The destination path of the result.")

parser.add_argument(
    "--langs", help="Language models to be used for the OCR. Examples: ckb or ckb+eng", default="ckb"
)

args = parser.parse_args()

if str.endswith(args.dest, ".txt"):
    result = pytesseract.image_to_string(
        Image.open(args.source), lang=args.langs)
    with open(args.dest, "w", encoding="utf-8") as f:
        f.write(result)
    print("Done :)")

elif str.endswith(args.dest, ".pdf"):
    pdf = pytesseract.image_to_pdf_or_hocr(
        args.source, extension='pdf', lang=args.langs)
    with open(args.dest, 'w+b') as f:
        f.write(pdf)  # pdf type is bytes by default
    print("Done :)")
else:
    print("Invalid format.")
    sys.exit(-1)

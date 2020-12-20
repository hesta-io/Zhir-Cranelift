import zhirpy
import argparse

parser = argparse.ArgumentParser(
    description="Pre-processes an image and prepares it for OCR."
)

parser.add_argument("source", help="The path for the source image.")

parser.add_argument("dest", help="The path for the cleaned-up image.")

args = parser.parse_args()

zhirpy.clean(args.source, args.dest)
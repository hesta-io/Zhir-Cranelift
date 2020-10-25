from functions import deskew, preprocess
from pathlib import Path
import plac

@plac.opt('output_file', "Optional output file", type=Path)
@plac.opt('input_file', "Input file", type=Path)
def main(input_file, output_file='.'):
    preprocess(input_file, output_file)

if __name__ == '__main__':
    plac.call(main)

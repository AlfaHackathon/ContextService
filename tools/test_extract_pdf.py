"""Independent layout fixtures; these are not model-quality evidence."""
import unittest
from PIL import Image, ImageDraw
from extract_pdf import grids, cell_words


class ExtractionTests(unittest.TestCase):
    def test_two_column_grid_keeps_every_row(self):
        image=Image.new('RGB',(1000,1400),'white')
        draw=ImageDraw.Draw(image)
        for x in [100,400,900]:draw.line((x,200,x,450),fill='black',width=2)
        for y in [200,250,300,350,400,450]:draw.line((100,y,900,y),fill='black',width=2)
        found=grids(image)
        self.assertEqual(len(found),1)
        self.assertEqual(len(found[0][0]),3)
        self.assertEqual(len(found[0][1]),6)

    def test_blank_page_is_not_a_table(self):
        self.assertEqual(grids(Image.new('RGB',(1000,1400),'white')),[])

    def test_neighbour_cell_cannot_supply_missing_value(self):
        words=[{'text':'7.5%','left':110,'top':210,'width':25,'height':15,'conf':92},
               {'text':'99%','left':410,'top':210,'width':25,'height':15,'conf':99}]
        self.assertEqual(cell_words(words,(100,200,400,250)),('7.5%',92))
        self.assertEqual(cell_words(words,(500,200,900,250)),('',0))


if __name__=='__main__':unittest.main()

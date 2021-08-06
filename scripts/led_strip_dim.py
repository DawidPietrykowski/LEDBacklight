import serial
import sys
global ser
if len(sys.argv) == 1:
    ser = serial.Serial('COM1', 9600)
if len(sys.argv) == 2:
    ser = serial.Serial(sys.argv[1], 9600)
if len(sys.argv) == 3:
    ser = serial.Serial(sys.argv[1], sys.argv[2])
values = bytearray([0, 0, 0, 8, 255])
ser.write(values)
ser.close()
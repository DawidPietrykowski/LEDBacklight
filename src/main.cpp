#include <Arduino.h>

#include <IRremote.h>
#include <TimerOne.h>
#include <CircularBuffer.h>
#include <FastLED.h>

#include "hex_codes.h"
#include "colors.h"

#define IR_RECEIVE_PIN  4
#define LED_PIN         6

#define LED_NUM         60

#define DEFAULT_MODE        4
#define BRIGHTNESS_STEP     15
#define DEFAULT_COLOR_MODE  8

#define SERIAL_BAUD_RATE    9600

#define GENERATE_RANDOM_FOR_SPRINKLE    false
#define SPRINKLE_UPDATE_INTERVAL        10
#define SPRINKLE_TRESHOLD               5
#define SPRINKLE_DIMMING_SPEED          1

#define POLICE_SPRINKLE_LIGHT_COUNT     15
#define POLICE_LIGHTS_PADDING           2
#define POLICE_SPRINKLE_UPDATE_INTERVAL 5

#define COLOR_SLIDE_IN_INTERVAL 2

#define DEBUG false
#define DEBUG_SERIAL if(DEBUG)Serial

byte main_r = 255;
byte main_g = 255;
byte main_b = 255;
byte main_brightness = 255;

CRGB leds[LED_NUM];
CircularBuffer<byte,LED_NUM> colorBufferR;
CircularBuffer<byte,LED_NUM> colorBufferG;
CircularBuffer<byte,LED_NUM> colorBufferB;

int serial_data_buffer[5];
bool light_pos_taken[LED_NUM];

const CRGB basic_colors_s[10] = {
CRGB(255,0,0),
CRGB(255,60,12),
CRGB(255,65,0),
CRGB(0,0,255),
CRGB(255,0,2),
CRGB(255,0,12),
CRGB(0,255,0),
CRGB(0,255,255),
CRGB(0,255,100),
CRGB(255,200,128)};
const byte color_count_s = 10;
const CRGB basic_colors_police_s[4] = {
CRGB(255,0,0),
CRGB(0,0,255),
CRGB(0,255,255),
CRGB(255,0,12)};
const byte color_count_police_s = 4;

byte mode = DEFAULT_MODE;
byte previous_mode = -1;

long lastmilis = 0;

int offset = 0;
int delay_time = 0;
int slide_in_position = 0;

uint32_t key_value = 0;

float quadraticTransition(float val){
    return (-4.0f*val*val)+(4.0f*val);
}

float lerp(float a, float b, float t){
    return a + t * (b - a);
}

bool freeSpot(int pos){
    if(light_pos_taken[pos])
        return false;

    for(int i = -POLICE_LIGHTS_PADDING; i <= POLICE_LIGHTS_PADDING; i++){
        if((pos+i>=0)&&(pos+i<=LED_NUM)){
        if(light_pos_taken[pos+i])
            return false;
        }
    }
    return true;
}

class Light {
private:
    CRGB main_color;
    int pos = 0;
    int step = 0;
    int steps = 500;
    byte stall_counter = 0;
public:
    Light(){
        leds[pos] = CRGB(0,0,0);
        light_pos_taken[pos] = false;
        do{
            stall_counter++;
            pos = random(LED_NUM);
        }while((!freeSpot(pos))&&(stall_counter<100));
        stall_counter = 0;
        light_pos_taken[pos] = true;
        main_color = basic_colors_police_s[random(color_count_police_s)];
        step = random(steps/2);
    }

    void reset() {
        leds[pos] = CRGB(0,0,0);
        light_pos_taken[pos] = false;
        do{
            stall_counter++;
            pos = random(LED_NUM);
        }while((!freeSpot(pos))&&(stall_counter<100));
        stall_counter = 0;
        light_pos_taken[pos] = true;

        main_color = basic_colors_police_s[random(color_count_police_s)];

        step = 0;
        update();
    }

    void update() {
        if(step<=steps){
            float mp = quadraticTransition((float)step/(float)steps);
            leds[pos] = CRGB((byte)(main_color.r*mp),(byte)(main_color.g*mp),(byte)(main_color.b*mp));
            step++;
        }
        else{
            reset();
        }
    }

    void reset_step(){
        step = random(steps/2);
    }
};

Light lights[POLICE_SPRINKLE_LIGHT_COUNT];

void setAllLeds(byte R,byte G,byte B){
    for(int i=0;i<LED_NUM;i++){
        leds[i].r = R;
        leds[i].g = G;
        leds[i].b = B;
    }
}

void resetLights(){
    setAllLeds(0,0,0);
    randomSeed(analogRead(random(0,5)));
    for(int i = 0; i < POLICE_SPRINKLE_LIGHT_COUNT; i++)
        lights[i].reset_step();
}

void staticColor(){
    setAllLeds(main_r,main_g,main_b);
}

void cycle(int d){
    if((delay_time%d==0)||(d==0)){
        colorBufferR.unshift(main_r);
        colorBufferG.unshift(main_g);
        colorBufferB.unshift(main_b);
        delay_time=0;
    }
    delay_time++;
    for(int i=0;i<LED_NUM;i++){
        leds[i].r = colorBufferR[i];
        leds[i].g = colorBufferG[i];
        leds[i].b = colorBufferB[i];
    }
}

void spread(int c, int d){
    if(delay_time%d==0){
        colorBufferR.unshift(main_r);
        colorBufferG.unshift(main_g);
        colorBufferB.unshift(main_b);
        delay_time=0;
    }
    delay_time++;
    for(int i=0;i<LED_NUM;i++){
        if(i>=c){
            leds[i].r = colorBufferR[i-c];
            leds[i].g = colorBufferG[i-c];
            leds[i].b = colorBufferB[i-c];
        }
        else{
            leds[i].r = colorBufferR[c-i];
            leds[i].g = colorBufferG[c-i];
            leds[i].b = colorBufferB[c-i];
        }
    }
}

void spread2(int c, int d){
    float a1 = 1/(float)c;
    float a2 = -1/(float)(59 - (float)c);
    if(delay_time%d==0){
        colorBufferR.unshift(main_r);
        colorBufferG.unshift(main_g);
        colorBufferB.unshift(main_b);
        delay_time=0;
    }
    delay_time++;
    for(int i=0;i<LED_NUM;i++){
        if(i>=c){
            leds[i].r = (float)colorBufferR[i-c]*(float)((a2*(i-c))+1);
            leds[i].g = (float)colorBufferG[i-c]*(float)((a2*(i-c))+1);
            leds[i].b = (float)colorBufferB[i-c]*(float)((a2*(i-c))+1);
        }
        else{
            leds[i].r = (float)colorBufferR[c-i]*(float)((a1*(i-c))+1);
            leds[i].g = (float)colorBufferG[c-i]*(float)((a1*(i-c))+1);
            leds[i].b = (float)colorBufferB[c-i]*(float)((a1*(i-c))+1);
        }
    }
}

void rainbow(bool mode){
    if(mode == 0){
        for(int i = 0; i<LED_NUM; i++)
            leds[i] = CHSV(offset+(i*6),225,255);
    }
    else if(mode == 1){
        for(int i = 0; i<LED_NUM; i++)
            leds[i] = CHSV(offset,225,255);
    }
}

void increment() {
    offset++;
}

void sprinkle(){
    for(int i=0;i<LED_NUM;i++){
#if GENERATE_RANDOM_FOR_SPRINKLE
        leds[i] = CRGB(random(255),random(255),random(255)).nscale8(50);
#else
        leds[i] = basic_colors_s[random(color_count_s)];
#endif
    }
}

void sprinkling(){
    for(int i=0;i<LED_NUM;i++){
        if(leds[i].getLuma() > SPRINKLE_TRESHOLD){
            if(leds[i].r > SPRINKLE_DIMMING_SPEED)
                leds[i].r -= SPRINKLE_DIMMING_SPEED;
            else
                leds[i].r = 0;

            if(leds[i].g > SPRINKLE_DIMMING_SPEED)
                leds[i].g -= SPRINKLE_DIMMING_SPEED;
            else
                leds[i].g = 0;

            if(leds[i].b > SPRINKLE_DIMMING_SPEED)
                leds[i].b -= SPRINKLE_DIMMING_SPEED;
            else
                leds[i].b = 0;
            }
        else{
            leds[i] = basic_colors_s[random(color_count_s)] - CRGB(random(15),random(15),random(15));
        }
    }
}

void decodeRemoteData(uint32_t data){
    switch(data){
        case REMOTE_CODE_R:
            DEBUG_SERIAL.println("R");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_R_R;
            main_g = COLOR_R_G;
            main_b = COLOR_R_B;
        break;
        case REMOTE_CODE_R1:
            DEBUG_SERIAL.println("R1");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_R1_R;
            main_g = COLOR_R1_G;
            main_b = COLOR_R1_B;
        break;
        case REMOTE_CODE_R2:
        DEBUG_SERIAL.println("R2");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_R2_R;
            main_g = COLOR_R2_G;
            main_b = COLOR_R2_B;
        break;
        case REMOTE_CODE_R3:
            DEBUG_SERIAL.println("R3");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_R3_R;
            main_g = COLOR_R3_G;
            main_b = COLOR_R3_B;
        break;
        case REMOTE_CODE_R4:
            DEBUG_SERIAL.println("R4");
            mode = DEFAULT_COLOR_MODE;
            main_r =COLOR_R4_R;
            main_g =COLOR_R4_G;
            main_b =COLOR_R4_B;
        break;
        case REMOTE_CODE_G:
            DEBUG_SERIAL.println("G");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_G_R;
            main_g = COLOR_G_G;
            main_b = COLOR_G_B;
        break;
        case REMOTE_CODE_G1:
            DEBUG_SERIAL.println("G1");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_G1_R;
            main_g = COLOR_G1_G;
            main_b = COLOR_G1_B;
        break;
        case REMOTE_CODE_G2:
            DEBUG_SERIAL.println("G2");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_G2_R;
            main_g = COLOR_G2_G;
            main_b = COLOR_G2_B;
        break;
        case REMOTE_CODE_G3:
            DEBUG_SERIAL.println("G3");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_G3_R;
            main_g = COLOR_G3_G;
            main_b = COLOR_G3_B;
        break;
        case REMOTE_CODE_G4:
            DEBUG_SERIAL.println("G4");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_G4_R;
            main_g = COLOR_G4_G;
            main_b = COLOR_G4_B;
        break;
        case REMOTE_CODE_B:
            DEBUG_SERIAL.println("B");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_B_R;
            main_g = COLOR_B_G;
            main_b = COLOR_B_B;
        break;
        case REMOTE_CODE_B1:
            DEBUG_SERIAL.println("B1");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_B1_R;
            main_g = COLOR_B1_G;
            main_b = COLOR_B1_B;
        break;
        case REMOTE_CODE_B2:
            DEBUG_SERIAL.println("B2");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_B2_R;
            main_g = COLOR_B2_G;
            main_b = COLOR_B2_B;
        break;
        case REMOTE_CODE_B3:
            DEBUG_SERIAL.println("B3");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_B3_R;
            main_g = COLOR_B3_G;
            main_b = COLOR_B3_B;
        break;
        case REMOTE_CODE_B4:
            DEBUG_SERIAL.println("B4");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_B4_R;
            main_g = COLOR_B4_G;
            main_b = COLOR_B4_B;
        break;
        case REMOTE_CODE_W:
            DEBUG_SERIAL.println("W");
            mode = DEFAULT_COLOR_MODE;
            main_r = COLOR_W_R;
            main_g = COLOR_W_G;
            main_b = COLOR_W_B;
        break;
        case REMOTE_CODE_FLASH:
            DEBUG_SERIAL.println("FLASH");
            mode = 3;
        break;
        case REMOTE_CODE_STROBE:
            DEBUG_SERIAL.println("STROBE");
            mode = 7;
        break;
        case REMOTE_CODE_FADE:
            DEBUG_SERIAL.println("FADE");
            mode = 4;
        break;
        case REMOTE_CODE_SMOOTH:
            DEBUG_SERIAL.println("SMOOTH");
            mode = 9;
        break;
        case REMOTE_CODE_BRIGHTNESS_DOWN:
            DEBUG_SERIAL.print("B-DOWN");
            DEBUG_SERIAL.print(' ');
            main_brightness = main_brightness > BRIGHTNESS_STEP ? main_brightness - BRIGHTNESS_STEP : 0;
            DEBUG_SERIAL.println(main_brightness);
        break;
        case REMOTE_CODE_BRIGHTNESS_UP:
            DEBUG_SERIAL.print("B-UP");
            DEBUG_SERIAL.print(' ');
            main_brightness = main_brightness < (255 - BRIGHTNESS_STEP) ? main_brightness + BRIGHTNESS_STEP : 255;
            DEBUG_SERIAL.println(main_brightness);
        break;
        case REMOTE_CODE_ON:
            DEBUG_SERIAL.println("ON");
            mode = DEFAULT_MODE;
        break;
        case REMOTE_CODE_OFF:
            DEBUG_SERIAL.println("OFF");
            mode = 0;
        break;
    }
}

void recvData() {
    if(Serial.available() == 5){
        for(int i = 0; i < 5; i++){
            serial_data_buffer[i] = Serial.read();
        }
        main_r = serial_data_buffer[0];
        main_g = serial_data_buffer[1];
        main_b = serial_data_buffer[2];
        mode = serial_data_buffer[3];
        main_brightness = serial_data_buffer[4];
        FastLED.setBrightness(main_brightness);
    }
}

void recvIRData(){
    while (!IrReceiver.isIdle());
    if (IrReceiver.decode()) {
        DEBUG_SERIAL.print("Raw Data: ");
        DEBUG_SERIAL.println(IrReceiver.decodedIRData.decodedRawData, HEX);
        DEBUG_SERIAL.print("\n\n");
        if (IrReceiver.decodedIRData.decodedRawData != 0)
            key_value = IrReceiver.decodedIRData.decodedRawData;

        decodeRemoteData(key_value);
        IrReceiver.resume();
    }
}

void setup() {
  Serial.begin(SERIAL_BAUD_RATE);
  FastLED.addLeds<WS2812B, LED_PIN, GRB>(leds, LED_NUM);
  FastLED.setBrightness(main_brightness);
  pinMode(13,OUTPUT);
  digitalWrite(13,LOW);
  IrReceiver.begin(IR_RECEIVE_PIN, false);
  Timer1.initialize(100000); // 1 second
  Timer1.attachInterrupt(increment);
  randomSeed(analogRead(0));
}

void loop() {
    // receive Serial data
    recvData();
    // receive IR data
    recvIRData();

    if(previous_mode!=mode){
        DEBUG_SERIAL.print("mode: ");
        DEBUG_SERIAL.println(mode);
        switch (mode){
            case 7:
            sprinkle();
            case 4:
            resetLights();
            case 8:
            slide_in_position = LED_NUM-1;
        }
        previous_mode=mode;
    }

    switch(mode){
        // turn leds off
        case 0:
            setAllLeds(0,0,0);
            break;

        // set all to one color
        case 1:
            staticColor();
            break;

        // cycle streamed colors
        case 2:
            cycle(0);
            break;

        // scrolling rainbow
        case 3:
            rainbow(0);
            break;

        // police sprinkle
        case 4:
            if(millis() - lastmilis > POLICE_SPRINKLE_UPDATE_INTERVAL){
                lastmilis = millis();
                for(int i = 0; i < POLICE_SPRINKLE_LIGHT_COUNT; i++){
                    lights[i].update();
                }
            }
            break;

        // spread streamed colors
        case 5:
            spread(37,3);
            break;

        // spread streamed colors
        case 6:
            spread2(37,2);
            break;

        // colorful sprinkle
        case 7:
            if(millis() - lastmilis > SPRINKLE_UPDATE_INTERVAL){
                lastmilis = millis();
                sprinkling();
            }
            break;

        // smoother color setting
        case 8:
        #if 1
            if(millis() - lastmilis > COLOR_SLIDE_IN_INTERVAL){
                lastmilis = millis();
                DEBUG_SERIAL.print("slide_in_position: ");
                DEBUG_SERIAL.println(slide_in_position);
                if(slide_in_position>=0){
                    leds[slide_in_position] = CRGB(main_r,main_g,main_b);
                    slide_in_position--;
                }
                else{
                    slide_in_position = LED_NUM-1;
                    mode = 1;
                }
            }
            break;
        #else
            DEBUG_SERIAL.print("slide_in_position: ");
            DEBUG_SERIAL.println(slide_in_position);
            if(slide_in_position<LED_NUM/2){
                if(slide_in_step<=3){
                    float r_v;
                    float g_v;
                    float b_v;
                    r_v = lerp((float)leds[slide_in_position].r, (float)r, ((float)slide_in_step)/3.0f);
                    g_v = lerp((float)leds[slide_in_position].g, (float)g, ((float)slide_in_step)/3.0f);
                    b_v = lerp((float)leds[slide_in_position].b, (float)b, ((float)slide_in_step)/3.0f);
                    leds[slide_in_position] = CRGB((int)r_v,(int)g_v,(int)b_v);

                    r_v = lerp((float)leds[LED_NUM-slide_in_position-1].r, (float)r, ((float)slide_in_step)/3.0f);
                    g_v = lerp((float)leds[LED_NUM-slide_in_position-1].g, (float)g, ((float)slide_in_step)/3.0f);
                    b_v = lerp((float)leds[LED_NUM-slide_in_position-1].b, (float)b, ((float)slide_in_step)/3.0f);
                    leds[LED_NUM-slide_in_position-1] = CRGB((int)r_v,(int)g_v,(int)b_v);
                    slide_in_step++;
                }
                else{
                    slide_in_position++;
                    slide_in_step = 0;
                }
            }
            else{
                slide_in_step = 0;
                slide_in_position = 0;
                mode = 1;
            }
            break;
        #endif


        // solid scrolling rainbow
        case 9:
            rainbow(1);
            break;
    }

    FastLED.setBrightness(main_brightness);
    FastLED.show();
}
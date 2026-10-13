from PIL import Image, ImageDraw, ImageFont
import json, math, subprocess, wave, pathlib, array
P=pathlib.Path(__file__).parent
W,H=720,1280
FPS=24
fonts={}
def f(n,b=False):
 k=(n,b)
 if k not in fonts: fonts[k]=ImageFont.truetype('C:/Windows/Fonts/msyh'+('bd' if b else '')+'.ttc',n)
 return fonts[k]
BG='#091322'; PANEL='#14243a'; WHITE='#f5f8ff'; MUTED='#9eafc8'; CYAN='#52e1cb'; BLUE='#578bff'; YELLOW='#ffca70'
def txt(d,xy,s,n=26,c=WHITE,b=False): d.text(xy,s,font=f(n,b),fill=c)
def box(d,xy,fill=PANEL,r=22,outline=None): d.rounded_rectangle(xy,r,fill=fill,outline=outline,width=2)
def center(d,y,s,n=26,c=WHITE,b=False): txt(d,((W-d.textlength(s,font=f(n,b)))/2,y),s,n,c,b)
def pill(d,x,y,s,c=CYAN):
 width=d.textlength(s,font=f(20))+30
 box(d,(x,y,x+width,y+38),'#1b3442',12)
 txt(d,(x+15,y+5),s,20,c)
def bar(d,x,y,w,p,c=CYAN):
 box(d,(x,y,x+w,y+12),'#2b3d54',6)
 if p>0: box(d,(x,y,x+max(12,w*p),y+12),c,6)
def card(d,y,title,sub):
 box(d,(44,y,676,y+100))
 txt(d,(68,y+15),title,26,WHITE,True);txt(d,(68,y+56),sub,21,MUTED)
def qr(d,x,y,size=200):
 # Decorative scan target, explicitly labeled as a schematic.
 box(d,(x-14,y-14,x+size+14,y+size+14),'white',12)
 unit=size/29
 for a in range(29):
  for b in range(29):
   if ((a*17+b*31+a*b)%11)<5:
    d.rectangle((x+a*unit,y+b*unit,x+(a+1)*unit,y+(b+1)*unit),fill=BG)
 for a,b in [(0,0),(22,0),(0,22)]:
  for off,col in [(0,BG),(1,'white'),(2,BG)]:
   d.rectangle((x+(a+off)*unit,y+(b+off)*unit,x+(a+7-off)*unit,y+(b+7-off)*unit),fill=col)
def frame(i,t,duration):
 im=Image.new('RGB',(W,H),BG);d=ImageDraw.Draw(im)
 for y in range(0,H,40): d.line((0,y,W,y),fill='#0c192b')
 d.ellipse((450,-190,970,330),fill='#112946')
 txt(d,(44,48),'小工单  /  车间进度看得见',22,MUTED)
 txt(d,(44,90),'流程模拟 · 示例数据',17,MUTED)
 titles=[('客户催交货','还得下车间问？'),('这张单做到哪了？','打开工单，直接查'),('完成一批，扫一下','找到对应工单'),('选工序 · 填数量','确认后提交报工'),('组装还差多少？','报工后，数量更新'),('交期到了，心里有数','关注临期订单'),('小工单','让车间进度看得见')]
 title,sub=titles[i]
 txt(d,(44,160),title,44,WHITE,True);txt(d,(44,225),sub,31,CYAN,True)
 if i==0:
  box(d,(44,355,676,540),'#20364e')
  txt(d,(75,390),'客户消息',22,MUTED)
  txt(d,(75,440),'这批货，什么时候能交？',33,WHITE,True)
  for j,(a,b) in enumerate([('找工单','翻记录'),('问车间','等回复'),('查进度','心里没底')]):
   y=595+j*130;box(d,(44,y,676,y+100));txt(d,(72,y+25),a,29,WHITE,True);txt(d,(435,y+27),b,26,YELLOW)
  center(d,1030,'每次催货，都要再问一遍？',28,MUTED)
 elif i in (1,4):
  card(d,320,'A0086  ·  五金支架','计划 100 件     工序进度明细')
  p=max(0,min(1,(t-1.0)/1.5)) if i==4 else 0
  val=60+round(20*p)
  for j,(name,num) in enumerate([('裁切',100),('检验',100),('组装',val),('包装',0)]):
   y=450+j*142;box(d,(44,y,676,y+120), '#183b49' if j==2 else PANEL)
   txt(d,(68,y+16),name,28,WHITE,True);txt(d,(407,y+18),f'{num} / 100 件',25,CYAN if j==2 else MUTED)
   bar(d,68,y+75,580,num/100)
  if i==4: center(d,1047,f'组装剩余  {100-val} 件',33,CYAN,True)
  else: center(d,1047,'各道工序，分别看清楚',28,MUTED)
 elif i==2:
  box(d,(145,335,575,1000),'#243a53',42);box(d,(160,350,560,982),'#0c192b',32)
  center(d,395,'扫描工单流转卡',28,WHITE,True)
  qr(d,260,525,200)
  scan=510+(t%2.5)/2.5*235
  d.line((230,scan,490,scan),fill=CYAN,width=4)
  center(d,776,'A0086  ·  五金支架',23,WHITE)
  center(d,825,'二维码示意',18,MUTED)
  if t>2.5: box(d,(198,884,522,944),'#174c49');center(d,899,'已定位工单 A0086',23,CYAN)
  center(d,1047,'手机 / PDA 扫码报工',28,MUTED)
 elif i==3:
  box(d,(104,320,616,1035),'#21344b',36);box(d,(119,335,601,1020),'#f3f6fa',27)
  txt(d,(147,370),'A0086 · 五金支架',28,'#162840',True)
  txt(d,(147,440),'工序',23,'#6d7b90');box(d,(143,480,577,550),'white',12);txt(d,(164,497),'组装',28,'#162840',True)
  txt(d,(147,595),'本次良品数量',23,'#6d7b90');box(d,(143,635,577,748),'white',12)
  center(d,647,'20' if t>1 else '0',58,'#326aea',True)
  txt(d,(147,780),'核对工单、工序和数量',22,'#6d7b90')
  box(d,(143,868,577,948),'#326aea',15);center(d,890,'提交报工' if t<3.5 else '提交成功',29,WHITE,True)
  if 2.3<t<3.5:
   rad=20+int((t%0.6)*32);d.ellipse((489-rad,908-rad,489+rad,908+rad),outline=CYAN,width=4)
  center(d,1070,'完成一批，报一批',28,MUTED)
 elif i==5:
  box(d,(44,330,676,660));pill(d,68,355,'临期提示',YELLOW)
  txt(d,(68,425),'A0086  五金支架',30,WHITE,True)
  txt(d,(68,490),'距计划交期',24,MUTED);txt(d,(435,471),'2 天',48,YELLOW,True)
  txt(d,(68,575),'组装剩余 20 件 · 包装待完成',26,WHITE)
  card(d,715,'支持本地部署','系统部署在工厂自己的服务器')
  card(d,845,'核心功能内网可用','下单 / 报工 / 查询生产进度')
  center(d,1045,'交期提示 ≠ 自动预测交货',21,MUTED)
 else:
  box(d,(44,340,676,640));center(d,395,'扫码报工',46,WHITE,True);center(d,485,'↓',38,CYAN);center(d,548,'工序进度有据可查',35,WHITE,True)
  box(d,(44,715,676,935),'#174c49');center(d,752,'私信「工单」',49,CYAN,True);center(d,837,'拿你的生产流程，看演示',29,WHITE)
  center(d,1020,'适用于需要跟踪工序进度的车间',23,MUTED)
 captions=[['客户催交货，还得下车间问？'],['哪道做完，哪道还差多少','打开工单，直接查'],['扫描流转卡','找到对应工单'],['选工序，填数量','确认后提交报工'],['组装已报：60 → 80 件','剩余：40 → 20 件'],['看板提示临期订单','支持本地部署、内网使用'],['私信「工单」','拿你的生产流程看演示']]
 box(d,(28,1120,692,1230),'#060e19',16)
 for j,line in enumerate(captions[i]):center(d,1139+j*39,line,27,WHITE,True)
 bar(d,44,1251,632,(i+t/duration)/7)
 fade=min(1,t/0.22,(duration-t)/0.18)
 if fade<1:im=Image.blend(Image.new('RGB',(W,H),BG),im,max(0,fade))
 return im
items=json.loads((P/'narration.json').read_text(encoding='utf-8-sig'))
durations=[]
for i,item in enumerate(items):
 with wave.open(str(P/f'voice-{i}.wav')) as w: sec=w.getnframes()/w.getframerate()
 durations.append(max(item['min'],sec+0.65))
(P/'timing.json').write_text(json.dumps(durations),encoding='utf8')
# Keep the original speech and pad each segment to its matching animation.
for i,dur in enumerate(durations):
 subprocess.run(['ffmpeg','-y','-loglevel','error','-i',str(P/f'voice-{i}.wav'),'-af','adelay=200,apad','-t',str(dur),'-ar','48000','-ac','2',str(P/f'audio-{i}.wav')],check=True)
(P/'audio-list.txt').write_text(''.join(f"file 'audio-{i}.wav'\n" for i in range(7)),encoding='utf8')
subprocess.run(['ffmpeg','-y','-loglevel','error','-f','concat','-safe','0','-i',str(P/'audio-list.txt'),'-c:a','pcm_s16le',str(P/'背景音乐.wav')],check=True)
out=P/'小工单_无真人推广视频.mp4'
proc=subprocess.Popen(['ffmpeg','-y','-loglevel','error','-f','rawvideo','-vcodec','rawvideo','-pix_fmt','rgb24','-s',f'{W}x{H}','-r',str(FPS),'-i','-','-i',str(P/'背景音乐.wav'),'-vf','scale=1080:1920','-c:v','libx264','-preset','fast','-crf','20','-pix_fmt','yuv420p','-c:a','aac','-b:a','160k','-movflags','+faststart','-shortest',str(out)],stdin=subprocess.PIPE)
srt=[];elapsed=0;thumbs=[]
def stamp(t):
 ms=round(t*1000);return f'{ms//3600000:02}:{ms//60000%60:02}:{ms//1000%60:02},{ms%1000:03}'
for i,dur in enumerate(durations):
 count=math.ceil(dur*FPS);dur=count/FPS
 preview=frame(i,dur*0.68,dur);preview.save(P/f'scene-{i+1}.jpg');thumbs.append(preview.resize((216,384)))
 srt.append(f'{i+1}\n{stamp(elapsed)} --> {stamp(elapsed+dur)}\n{items[i]["text"]}\n');elapsed+=dur
 for k in range(count):proc.stdin.write(frame(i,k/FPS,dur).tobytes())
 print(f'Scene {i+1}/7 rendered',flush=True)
proc.stdin.close()
if proc.wait()!=0:raise RuntimeError('Video encoding failed')
(P/'字幕.srt').write_text('\n'.join(srt),encoding='utf8')
contact=Image.new('RGB',(216*4,384*2),BG)
for i,im in enumerate(thumbs):contact.paste(im,((i%4)*216,(i//4)*384))
contact.save(P/'分镜预览.jpg')
frame(0,2,4.2).resize((1080,1920)).save(P/'封面.jpg')
print(str(out),round(elapsed,2),flush=True)


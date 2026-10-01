from PIL import Image,ImageDraw,ImageFont
import numpy as np,cv2,pathlib,subprocess,math,wave,array,sys
P=pathlib.Path(__file__).parent;W,H=720,1280;FPS=24
source=Image.open(P/'phone-scene.png').convert('RGB');SW,SH=source.size
base=np.array(source);manager=Image.open(P.parent/'promo-video-v2/manager.png').convert('RGB')
fonts={}
def font(n,b=False):
 if (n,b) not in fonts:fonts[n,b]=ImageFont.truetype('C:/Windows/Fonts/msyh'+('bd' if b else '')+'.ttc',n)
 return fonts[n,b]
def text(d,x,y,s,n=26,c='#12263a',b=False):d.text((x,y),s,font=font(n,b),fill=c)
def box(d,xy,c,r=14):d.rounded_rectangle(xy,r,fill=c)
def mid(d,y,s,n=26,c='white',b=False):text(d,(720-d.textlength(s,font=font(n,b)))/2,y,s,n,c,b)
def ease(a):a=max(0,min(1,a));return a*a*(3-2*a)
# Four measured screen corners in the generated scene; keep the original bezel and finger in front.
quad=np.float32([[471,615],[655,626],[615,1058],[432,1047]])
M=cv2.getPerspectiveTransform(np.float32([[0,0],[359,0],[359,799],[0,799]]),quad)
mask=Image.new('L',(360,800));ImageDraw.Draw(mask).rounded_rectangle((0,0,359,799),24,fill=255)
alpha=cv2.warpPerspective(np.array(mask),M,(SW,SH)).astype(np.float32)/255
# Foreground fingertip/hand occlusion, traced from the source image.
occ=np.zeros((SH,SW),np.uint8)
cv2.fillPoly(occ,[np.int32([[420,1057],[433,1017],[449,986],[461,961],[470,956],[477,963],[476,976],[469,997],[457,1021],[445,1045],[440,1060]])],255)
alpha*=1-occ/255
alpha=alpha[:,:,None]
qrpic=source.crop((838,713,915,802)).resize((210,230),Image.Resampling.LANCZOS)
def screen(t):
 im=Image.new('RGB',(360,800),'#f1f5fa');d=ImageDraw.Draw(im)
 box(d,(0,0,360,99),'#143348',0);text(d,20,15,'小工单',22,'white',True);text(d,20,51,'扫码报工',27,'white',True)
 if t<4:
  box(d,(12,112,348,650),'#172d3a',12);text(d,48,137,'对准流转卡二维码',25,'white',True)
  im.paste(qrpic,(75,257));d=ImageDraw.Draw(im)
  sy=240+(t%1.7)/1.7*255;d.line((57,sy,303,sy),fill='#52e1cb',width=4)
  text(d,62,549,'正在识别工单…',26,'#52e1cb',True)
  text(d,47,694,'手机摄像头扫码',25,'#687b8e')
 else:
  text(d,18,116,'A0086 · 五金支架',27,'#152c43',True)
  if t<19:
   box(d,(15,166,345,259),'white');text(d,30,179,'计划 100 件',24,'#61748a');text(d,30,218,'组装已报 60 件',24,'#61748a')
   text(d,20,288,'工序',24,'#61748a');box(d,(15,327,345,391),'white');text(d,31,342,'组装',29,'#12263a',True)
   if 6<t<8.5:box(d,(18,398,343,457),'#deebff');text(d,35,411,'已选择：组装',25,'#326aea')
   text(d,20,480,'本次良品数量',24,'#61748a');box(d,(15,523,345,630),'white')
   number='20' if t>10 else ('2' if t>9 else '0');text(d,138,535,number,54,'#326aea',True)
   box(d,(15,683,345,754),'#326aea');text(d,110,701,'提交报工',28,'white',True)
   if 13<t<15:
    rr=18+int((t%0.7)*22);d.ellipse((263-rr,718-rr,263+rr,718+rr),outline='#52e1cb',width=4)
   if t>=15:
    box(d,(26,363,334,554),'#123c43',20);text(d,85,398,'提交成功',35,'#52e1cb',True);text(d,60,466,'本次组装 +20 件',27,'white')
  else:
   text(d,20,165,'工序进度',28,'#12263a',True)
   for j,(name,num) in enumerate([('裁切',100),('检验',100),('组装',80),('包装',0)]):
    y=213+j*103;box(d,(15,y,345,y+91),'white');text(d,28,y+8,name,26,'#12263a',True);text(d,176,y+11,f'{num}/100',25,'#326aea')
    box(d,(28,y+61,330,y+72),'#e1e9f2',5)
    if num:box(d,(28,y+61,28+302*num/100,y+72),'#326aea',5)
   box(d,(15,655,345,762),'#e4f3ef');text(d,33,675,'组装剩余 20 件',30,'#126d63',True)
   text(d,33,722,'计划交期：2 天后',23,'#536a7b')
 return np.array(im)
shade=Image.new('RGBA',(720,1280));dd=ImageDraw.Draw(shade)
for y in range(1280):
 a=int(max(0,1-y/300)*150+max(0,(y-1000)/280)*220);dd.line((0,y,720,y),fill=(3,12,22,min(230,a)))
def camera(composite,t):
 z=1+1.45*ease((t-1)/7)
 cw=SW/z;ch=SH/z
 # Smooth continuous push toward the phone, keeping its screen central.
 cx=SW/2+(543-SW/2)*ease((t-1)/7);cy=SH/2
 crop=(cx-cw/2,cy-ch/2,cx+cw/2,cy+ch/2)
 im=Image.fromarray(composite).resize((720,1280),Image.Resampling.BICUBIC,box=crop)
 return Image.alpha_composite(im.convert('RGBA'),shade).convert('RGB')
def render(t):
 if t<3:
  z=1+.025*t/3;cw=manager.width/z;ch=manager.height/z
  im=manager.resize((720,1280),Image.Resampling.BICUBIC,box=((manager.width-cw)/2,(manager.height-ch)/2,(manager.width+cw)/2,(manager.height+ch)/2))
  im=Image.alpha_composite(im.convert('RGBA'),shade).convert('RGB');d=ImageDraw.Draw(im)
  text(d,44,162,'客户又催交货了',45,'white',True);text(d,44,226,'这张单，到底做到哪？',31,'#52e1cb',True)
  box(d,(44,1043,676,1167),'#132638',20);text(d,66,1067,'“这批货，什么时候能交？”',29,'white',True)
 else:
  tt=t-3
  warped=cv2.warpPerspective(screen(tt),M,(SW,SH))
  merged=(warped*alpha+base*(1-alpha)).astype(np.uint8)
  im=camera(merged,tt);d=ImageDraw.Draw(im)
  if tt<4:head,sub='拿起手机，扫工单','扫描流转卡，定位工单'
  elif tt<8.5:head,sub='选中这道工序','A0086 · 组装'
  elif tt<13:head,sub='完成一批，填数量','本次良品：20 件'
  elif tt<19:head,sub='确认数量，提交报工','提交成功，本次组装 +20 件'
  elif tt<25:head,sub='数量更新，还差多少？','组装已报 80 件 · 剩余 20 件'
  else:head,sub='小工单，让进度看得见','私信「工单」，拿你的流程看演示'
  text(d,35,156,head,37,'white',True)
  box(d,(25,1112,695,1222),'#102d3c',19);mid(d,1143,sub,27,'#ffffff',True)
 d=ImageDraw.Draw(im);text(d,35,39,'小工单  /  车间进度看得见',21,'#e0e8ef')
 text(d,35,81,'AI 情境演绎 · 屏幕流程模拟 · 示例数据',17,'#c9d9e6')
 return im
if '--preview' in sys.argv:
 for i,t in enumerate([1.5,5,10,14,19,25,32]):render(t).save(P/f'preview-{i+1}.jpg')
 sys.exit()
# Reuse the original continuous music bed, without reintroducing the previous video cuts.
audio=P.parent/'promo-video-v2/配乐.wav'
out=P/'小工单_手机屏内连续演示.mp4'
proc=subprocess.Popen(['ffmpeg','-y','-loglevel','error','-f','rawvideo','-pix_fmt','rgb24','-s','720x1280','-r','24','-i','-','-i',str(audio),'-vf','scale=1080:1920','-c:v','libx264','-preset','fast','-crf','19','-pix_fmt','yuv420p','-c:a','aac','-b:a','160k','-movflags','+faststart','-t','34',str(out)],stdin=subprocess.PIPE)
last=render(2.999)
for k in range(34*FPS):
 t=k/FPS;im=render(t)
 if 3<=t<3.35:im=Image.blend(last,im,(t-3)/.35)
 if t>33.5:im=Image.blend(im,Image.new('RGB',(720,1280),'#091322'),(t-33.5)/.5)
 proc.stdin.write(im.tobytes())
 if k%120==0:print(f'{int(t)}/34 seconds rendered',flush=True)
proc.stdin.close()
if proc.wait():raise RuntimeError('Video encoding failed')
thumbs=[]
for i,t in enumerate([1.5,5,10,14,19,25,32]):
 im=render(t);im.save(P/f'preview-{i+1}.jpg');thumbs.append(im.resize((216,384)))
contact=Image.new('RGB',(864,768),'#091322')
for i,im in enumerate(thumbs):contact.paste(im,((i%4)*216,(i//4)*384))
contact.save(P/'分镜总览.jpg');render(14).resize((1080,1920)).save(P/'封面.jpg')
print('Complete',flush=True)

from PIL import Image,ImageDraw
import pathlib,numpy as np,cv2,subprocess,wave,math,sys
P=pathlib.Path(__file__).parent;W,H=720,1280;FPS=24;DUR=44
old=P.parent/'promo-video-v3/render_phone.py';ns={'__file__':str(old)}
exec(old.read_text(encoding='utf-8-sig').split("if '--preview'")[0],ns)
text=ns['text'];box=ns['box'];mid=ns['mid'];ease=ns['ease'];font=ns['font']
busy=Image.open(P/'busy.png').convert('RGB');check=Image.open(P/'check.png').convert('RGB');ride=Image.open(P/'ride.png').convert('RGB')
worker=ns['source'];workerM=ns['M'];workerMask=ns['alpha'][:,:,0]
corners=np.float32([[468,615],[631,627],[600,998],[426,984]])
checkM=cv2.getPerspectiveTransform(np.float32([[0,0],[359,0],[359,799],[0,799]]),corners)
mask=Image.new('L',(360,800));ImageDraw.Draw(mask).rounded_rectangle((0,0,359,799),25,fill=255)
checkMask=cv2.warpPerspective(np.array(mask),checkM,check.size).astype(np.float32)/255
# Light darkening only in caption areas; preserve cinematic source lighting.
shade=Image.new('RGBA',(W,H));d=ImageDraw.Draw(shade)
for y in range(H):
 a=int(max(0,1-y/290)*155+max(0,(y-1000)/280)*220)
 d.line((0,y,W,y),fill=(5,14,22,min(225,a)))
def grade(im):return Image.alpha_composite(im.convert('RGBA'),shade).convert('RGB')
def photo(src,z=1,cx=.5,cy=.5):
 cw=src.width/z;ch=src.height/z;x=src.width*cx;y=src.height*cy
 x=max(cw/2,min(src.width-cw/2,x));y=max(ch/2,min(src.height-ch/2,y))
 return grade(src.resize((W,H),Image.Resampling.BICUBIC,box=(x-cw/2,y-ch/2,x+cw/2,y+ch/2)))
def nested(src,mat,mask,ui,z,cx,cy):
 cw=src.width/z;ch=src.height/z;cx=max(cw/2,min(src.width-cw/2,cx));cy=max(ch/2,min(src.height-ch/2,cy));x0=cx-cw/2;y0=cy-ch/2
 T=np.array([[W/cw,0,-x0*W/cw],[0,H/ch,-y0*H/ch],[0,0,1]],dtype=np.float32)
 bg=np.array(src.resize((W,H),Image.Resampling.BICUBIC,box=(x0,y0,x0+cw,y0+ch)))
 overlay=cv2.warpPerspective(ui,T@mat,(W,H),flags=cv2.INTER_CUBIC)
 alpha=cv2.warpPerspective(mask,T,(W,H),flags=cv2.INTER_LINEAR)[:,:,None]
 merged=np.clip(overlay*alpha+bg*(1-alpha),0,255).astype(np.uint8)
 return grade(Image.fromarray(merged))
def progress(t):
 im=Image.new('RGB',(360,800),'#f1f5fa');d=ImageDraw.Draw(im)
 box(d,(0,0,360,101),'#143348',0);text(d,20,14,'小工单',22,'white',True);text(d,20,51,'生产进度',28,'white',True)
 text(d,18,121,'A0086 · 五金支架',27,'#183248',True);text(d,18,163,'计划 100 件 · 执行中',23,'#627689')
 num=60 if t<3.7 else 80
 for j,(name,n) in enumerate([('裁切',100),('检验',100),('组装',num),('包装',0)]):
  y=210+j*103;box(d,(15,y,345,y+92),'#e3f2ee' if j==2 else 'white')
  text(d,28,y+9,name,26,'#163345',True);text(d,175,y+12,f'{n}/100',25,'#326aea')
  box(d,(28,y+62,330,y+73),'#dce5ee',5)
  if n:box(d,(28,y+62,28+302*n/100,y+73),'#326aea',5)
 box(d,(15,655,345,771),'#dff1eb');text(d,31,670,f'组装剩余 {100-num} 件',29,'#126d63',True)
 text(d,31,720,'计划交期：2 天后',24,'#53697b')
 return np.array(im)
def caption(im,heading,sub,brand=True):
 d=ImageDraw.Draw(im)
 text(d,36,40,'小工单',24,'#edf3f7',True)
 text(d,36,81,'AI 情境演绎 · 示例数据',17,'#d0dce5')
 if heading:text(d,36,161,heading,39,'white',True)
 if sub:
  box(d,(25,1117,695,1226),'#112c3b',18);mid(d,1150,sub,27,'white',True)
 return im
# Four acts: pressure, field reporting, owner checks phone, freedom.
def frame(t):
 if t<7:
  im=photo(busy,1.02+.12*t/7,.49,.48)
  if t<2.3:head,sub='刚想走，又被叫住','“这批货，什么时候能交？”'
  elif t<4.7:head,sub='问完这边，再问那边','“组装做了多少？还差多少？”'
  else:head,sub='一天都在追着进度跑','每一次催货，都得再问一遍'
  return caption(im,head,sub)
 if t<21:
  tt=t-7;st=tt*18.8/14;z=1+1.38*ease(tt/5)
  cx=worker.width/2+(543-worker.width/2)*ease(tt/5)
  im=nested(worker,workerM,workerMask,ns['screen'](st),z,cx,worker.height/2)
  if st<4:head,sub='把现场的进度，记下来','工人手机扫码，找到工单'
  elif st<9:head,sub='完成一批，现场报','选择工序：组装'
  elif st<15:head,sub='完成一批，现场报','本次良品：20 件'
  else:head,sub='进度有了来处','报工提交成功'
  return caption(im,head,sub)
 if t<34:
  tt=t-21;z=1+1.5*ease(tt/4.8);cx=check.width/2+(533-check.width/2)*ease(tt/4.8)
  im=nested(check,checkM,checkMask,progress(tt),z,cx,815)
  if tt<4:head,sub='拿起手机，进度就在眼前','厂内手机查看 · 同一张工单'
  elif tt<8.5:head,sub='做到哪，还差多少','组装已报 80 件 · 剩余 20 件'
  else:head,sub='看清楚，心里才有数','各道工序、剩余数量、计划交期'
  return caption(im,head,sub)
 tt=t-34;im=photo(ride,1.16-.14*ease(tt/10),.5,.54);d=ImageDraw.Draw(im)
 text(d,36,43,'小工单',26,'#163248',True);text(d,36,85,'AI 情境演绎',17,'#264254')
 if tt<3:
  text(d,36,177,'厂里的事，有数。',45,'#163248',True)
 elif tt<6:
  text(d,36,177,'自己的生活，有空。',43,'#163248',True)
 else:
  text(d,36,164,'厂里的事有数',44,'#163248',True);text(d,36,230,'自己的生活有空',44,'#163248',True)
  box(d,(35,1105,685,1227),'#102c39',20);mid(d,1124,'小工单 · 手机查生产进度',29,'white',True);mid(d,1171,'私信「工单」，看你的工厂怎么用',23,'#c8dedf')
 return im
# Original sound design: interrupted opening -> spacious underscore -> engine/wind departure.
rate=24000;tt=np.arange(DUR*rate,dtype=np.float64)/rate
rng=np.random.default_rng(18);noise=rng.normal(0,1,len(tt));sig=np.zeros_like(tt)
for begin in [0.5,2.5,4.8]:
 dt=tt-begin;env=np.where((dt>0)&(dt<.55),np.exp(-np.maximum(0,dt)*5),0)
 sig+=.07*env*(np.sin(2*np.pi*880*tt)+np.sin(2*np.pi*1174*tt))
# Smooth evolving pads and a plucked arpeggio, brighter after the phone-check scene.
roots=np.choose(((tt//4).astype(int)%4),[146.832,130.813,164.814,196.0]);phase=np.cumsum(roots)/rate
volume=np.where(tt<7,.008,np.where(tt<34,.025,.036))
for ratio in [1,1.25,1.5]:sig+=volume*np.sin(2*np.pi*phase*ratio)
beat=tt%.5;sig+=np.where((tt>7)&(tt<34),.05,0)*np.sin(2*np.pi*60*beat)*np.exp(-beat*22)
notes=np.choose(((tt*2).astype(int)%8),[293.66,369.99,440,587.33,440,369.99,329.63,440])
sig+=np.where(tt>7,.025,0)*np.sin(2*np.pi*np.cumsum(notes)/rate)*np.exp(-beat*7)
for begin in [9.7,18.2,24.7]:
 dt=tt-begin;sig+=np.where((dt>0)&(dt<.13),.06*np.sin(2*np.pi*1250*tt)*np.sin(np.pi*np.clip(dt,0,.13)/.13),0)
engine=np.clip((tt-34)/1.5,0,1)*np.clip((44-tt)/3,0,1)
freq=38+15*np.clip((tt-34)/7,0,1);ph=np.cumsum(freq)/rate
sig+=engine*(.06*np.sin(2*np.pi*ph)+.03*np.sin(2*np.pi*ph*2)+.012*noise)
sig*=np.minimum(1,tt/.15)*np.minimum(1,(44-tt)/1.3)
with wave.open(str(P/'广告配乐.wav'),'wb') as w:w.setparams((1,2,rate,0,'NONE','not compressed'));w.writeframes((np.clip(sig,-.9,.9)*32767).astype('<i2').tobytes())
keytimes=[1.5,5,10,17,23,28,33,38,42]
def previews():
 contact=Image.new('RGB',(216*3,384*3),'#091322')
 for i,t in enumerate(keytimes):
  im=frame(t);im.save(P/f'preview-{i+1}.jpg');contact.paste(im.resize((216,384)),((i%3)*216,(i//3)*384))
 contact.save(P/'分镜总览.jpg');frame(42).resize((1080,1920)).save(P/'封面.jpg')
if '--preview' in sys.argv:previews();sys.exit()
out=P/'小工单_生活有空_交付版.mp4'
proc=subprocess.Popen(['ffmpeg','-y','-loglevel','error','-f','rawvideo','-pix_fmt','rgb24','-s','720x1280','-r','24','-i','-','-i',str(P/'广告配乐.wav'),'-vf','scale=1080:1920','-c:v','libx264','-preset','fast','-crf','19','-pix_fmt','yuv420p','-c:a','aac','-b:a','160k','-movflags','+faststart','-t',str(DUR),str(out)],stdin=subprocess.PIPE)
transitions={a:frame(a-.001) for a in [7,21,34]}
for k in range(DUR*FPS):
 t=k/FPS;im=frame(t)
 for a,last in transitions.items():
  if a<=t<a+.4:im=Image.blend(last,im,ease((t-a)/.4))
 if t>43.5:im=Image.blend(im,Image.new('RGB',(W,H),'#091322'),(t-43.5)/.5)
 proc.stdin.write(im.tobytes())
 if k%120==0:print(f'{int(t)}/{DUR} seconds',flush=True)
proc.stdin.close()
if proc.wait():raise RuntimeError('Encoding failed')
previews();print('Complete',flush=True)



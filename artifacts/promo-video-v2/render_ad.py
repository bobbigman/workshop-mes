from PIL import Image,ImageDraw
import pathlib,math,subprocess,wave,array
P=pathlib.Path(__file__).parent
old=P.parent/'promo-video'/'render_video.py'
ns={'__file__':str(old)}
exec(old.read_text(encoding='utf-8-sig').split('items=json.loads')[0],ns)
frame0=ns['frame'];txt=ns['txt'];box=ns['box'];center=ns['center'];f=ns['f']
W,H=720,1280;FPS=24
manager=Image.open(P/'manager.png').convert('RGB');worker=Image.open(P/'worker.png').convert('RGB')
shots=[('manager',5.0),('ui1',4.0),('worker',5.0),('ui3',4.5),('ui4',5.5),('ui5',5.0),('end',5.0)]
duration=sum(x[1] for x in shots)
# An original, continuous low-key underscore with transition pulses and a scanner cue.
rate=24000;pcm=array.array('h');starts=[];elapsed=0
for name,dur in shots:starts.append(elapsed);elapsed+=dur
for k in range(int(duration*rate)):
 t=k/rate;beat=t%0.5
 roots=[146.832,130.813,164.814,130.813];root=roots[int(t/4)%4]
 v=sum(math.sin(2*math.pi*root*r*t) for r in (1,1.189207,1.498307))*.018
 v+=math.sin(2*math.pi*root*.5*t)*.025
 v+=math.sin(2*math.pi*(50*beat+1.5*(1-math.exp(-beat*30))))*math.exp(-beat*23)*.13
 v+=math.sin(2*math.pi*880*t)*math.exp(-(t%.25)*52)*.015
 for cue in (12.4,18.0):
  z=t-cue
  if 0<z<.16:v+=math.sin(2*math.pi*1174*z)*.09*math.sin(math.pi*z/.16)
 env=min(1,t/.8,(duration-t)/1.2)
 pcm.append(int(max(-1,min(1,v*max(0,env)))*32767))
with wave.open(str(P/'配乐.wav'),'wb') as a:a.setparams((1,2,rate,0,'NONE','not compressed'));a.writeframes(pcm.tobytes())
# Video compositing: generated keyframes with slow virtual-camera movement and title overlays.
shade=Image.new('RGBA',(W,H));sd=ImageDraw.Draw(shade)
for y in range(H):
 alpha=int(max(0,1-y/400)*140+max(0,(y-780)/500)*205)
 sd.line((0,y,W,y),fill=(4,12,22,min(230,alpha)))
def photo(src,p):
 scale=max(W/src.width,H/src.height)*(1.01+.055*p)
 ww,hh=round(src.width*scale),round(src.height*scale)
 image=src.resize((ww,hh),Image.Resampling.BICUBIC)
 x=round((ww-W)*(.45+.12*p));y=round((hh-H)*.45)
 return Image.alpha_composite(image.crop((x,y,x+W,y+H)).convert('RGBA'),shade).convert('RGB')
def render(name,t,dur):
 p=t/dur
 if name.startswith('ui'):
  idx=int(name[2:]);im=frame0(idx,min(dur-.22,max(.24,t)),dur)
  d=ImageDraw.Draw(im);box(d,(30,37,690,125),'#091322',4)
  txt(d,(44,45),'小工单  /  车间进度看得见',22,'#9eafc8')
  txt(d,(44,87),'产品流程模拟 · 示例数据',18,'#9eafc8')
  box(d,(0,1240,720,1280),'#091322',0)
  if idx==5:
   box(d,(35,146,700,222),'#091322',0);txt(d,(44,160),'快到交期，及时关注',42,'#f5f8ff',True)
 else:
  im=photo(manager if name=='manager' else worker,p);d=ImageDraw.Draw(im)
  txt(d,(44,48),'小工单  /  车间进度看得见',22,'#e2e8ee')
  box(d,(44,93,329,129),'#132638',9);txt(d,(55,99),'AI 生成画面 · 情境演绎',18,'#d1deeb')
  if name=='manager':
   txt(d,(44,170),'客户又催交货了',47,'#ffffff',True)
   txt(d,(44,238),'这张单，到底做到哪？',33,'#52e1cb',True)
   box(d,(44,971,676,1100),'#132638',20)
   txt(d,(69,990),'客户消息',19,'#9eafc8');txt(d,(69,1030),'“这批货，什么时候能交？”',29,'#ffffff',True)
   center(d,1154,'还得下车间，挨个问？',31,'#ffffff',True)
  elif name=='worker':
   txt(d,(44,174),'完成一批，现场报',45,'#ffffff',True)
   txt(d,(44,244),'扫码找到工单',33,'#52e1cb',True)
   box(d,(44,1025,676,1117),'#132638',20)
   txt(d,(67,1051),'扫码定位  →  选工序  →  填数量',27,'#ffffff',True)
   center(d,1160,'把现场完成的数量，记录下来',27,'#ffffff',True)
  else:
   txt(d,(44,173),'小工单',64,'#ffffff',True)
   txt(d,(44,266),'让车间进度看得见',36,'#52e1cb',True)
   box(d,(44,949,676,1141),'#112e38',24)
   center(d,975,'私信「工单」',47,'#52e1cb',True)
   center(d,1056,'拿你的生产流程，看演示',29,'#ffffff',True)
   center(d,1180,'扫码报工  /  工序进度  /  交期提示',21,'#e2e8ee')
 return im
out=P/'小工单_AI人物广告版.mp4'
cmd=['ffmpeg','-y','-loglevel','error','-f','rawvideo','-pix_fmt','rgb24','-s',f'{W}x{H}','-r',str(FPS),'-i','-','-i',str(P/'配乐.wav'),'-vf','scale=1080:1920','-c:v','libx264','-preset','fast','-crf','19','-pix_fmt','yuv420p','-c:a','aac','-b:a','160k','-movflags','+faststart','-shortest',str(out)]
proc=subprocess.Popen(cmd,stdin=subprocess.PIPE)
thumbs=[];previous=None
for i,(name,dur) in enumerate(shots):
 preview=render(name,dur*.7,dur);preview.save(P/f'scene-{i+1}.jpg');thumbs.append(preview.resize((216,384)))
 for k in range(round(dur*FPS)):
  t=k/FPS;im=render(name,t,dur)
  if previous is not None and t<.3:im=Image.blend(previous,im,t/.3)
  if i==len(shots)-1 and t>dur-.45:im=Image.blend(im,Image.new('RGB',(W,H),'#091322'),(t-dur+.45)/.45)
  proc.stdin.write(im.tobytes())
 previous=render(name,dur-.05,dur)
 print(f'Scene {i+1}/7 rendered',flush=True)
proc.stdin.close()
if proc.wait()!=0:raise RuntimeError('Video encoder failed')
contact=Image.new('RGB',(864,768),'#091322')
for i,im in enumerate(thumbs):contact.paste(im,((i%4)*216,(i//4)*384))
contact.save(P/'分镜总览.jpg')
render('manager',2,5).resize((1080,1920)).save(P/'封面.jpg')
print('Complete',duration,flush=True)

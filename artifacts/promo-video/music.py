import pathlib,json,wave,math,array
p=pathlib.Path('artifacts/promo-video')
items=json.loads((p/'narration.json').read_text(encoding='utf-8-sig'))
for i,item in enumerate(items):
 dur=item['min']-.65
 rate=24000
 samples=array.array('h')
 for k in range(int(dur*rate)):
  t=k/rate
  beat=t%.5
  chord=[146.83,174.61,220.0] if i%2==0 else [130.81,164.81,196.0]
  pad=sum(math.sin(2*math.pi*freq*t) for freq in chord)*.019
  kick=math.sin(2*math.pi*(58*beat+2*(1-math.exp(-beat*22))))*math.exp(-beat*20)*.10
  pulse=math.sin(2*math.pi*440*t)*math.exp(-(t%.25)*42)*.022
  envelope=min(1,t/.2,(dur-t)/.3)
  samples.append(int(32767*(pad+kick+pulse)*max(0,envelope)))
 with wave.open(str(p/f'voice-{i}.wav'),'wb') as w:
  w.setparams((1,2,rate,0,'NONE','not compressed'));w.writeframes(samples.tobytes())

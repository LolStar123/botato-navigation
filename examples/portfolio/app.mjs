import {W,H,makeMap,route,paint,lineCells} from './model.mjs';
const $=s=>document.querySelector(s),canvas=$('#world'),ctx=canvas.getContext('2d'),S=12;
const ground=document.createElement('canvas');ground.width=960;ground.height=600;const g=ground.getContext('2d');
let map,actor,goal,result,index=1,tool='goal',paused=false,drag=false,last=0,travel=0,drawDirty=true;
const center=p=>[(p[0]+.5)*S,(p[1]+.5)*S];
function updateGround(){
    g.fillStyle='#202c29';g.fillRect(0,0,960,600);
    for(let i=0;i<130;i++){const x=(i*173+27)%960,y=(i*97+17)%600;g.strokeStyle='#2c3b35';g.beginPath();g.moveTo(x-2,y+2);g.lineTo(x,y-3);g.lineTo(x+2,y+1);g.stroke()}
    for(const k of map.blocked){const x=k%W,y=Math.floor(k/W);g.fillStyle='#444f46';g.fillRect(x*S,y*S,S,S);g.strokeStyle='#7e8a77';g.lineWidth=1;g.beginPath();for(const [dx,dy,a,b,c,d] of [[0,-1,0,0,S,0],[0,1,0,S,S,S],[-1,0,0,0,0,S],[1,0,S,0,S,S]])if(!map.blocked.has((y+dy)*W+x+dx)){g.moveTo(x*S+a,y*S+b);g.lineTo(x*S+c,y*S+d)}g.stroke();if((x*11+y*7)%17===0){g.strokeStyle='#303c35';g.beginPath();g.moveTo(x*S,y*S+3);g.lineTo(x*S+7,y*S+7);g.lineTo(x*S+4,y*S+11);g.stroke()}}
    drawDirty=true;
}
function plan(){
    actor=actor.map(Math.round);const t=performance.now();result=route(map.blocked,actor,goal,Number($('#clearance').value));const elapsed=performance.now()-t;index=1;
    const distance=result.path.slice(1).reduce((sum,p,i)=>sum+Math.hypot(p[0]-result.path[i][0],p[1]-result.path[i][1]),0);
    $('#distance').textContent=distance.toFixed(1)+' cells';$('#nodes').textContent=result.visited.length;$('#turns').textContent=Math.max(0,result.path.length-2);$('#timing').textContent=elapsed.toFixed(1)+' ms';$('#clearance-value').textContent=$('#clearance').value+' cells';
    $('#status').textContent=result.path.length?'Route found. Following a collision-checked path.':'No route. Erase a blockage or choose an open destination.';
    window.__botato={ready:true,path:result.path,expanded:result.visited.length,actor:[...actor],goal:[...goal],blocked:map.blocked.size,reachable:!!result.path.length};drawDirty=true;
}
function reset(){map=makeMap($('#map').value);actor=[...map.start];goal=[...map.goal];travel=0;updateGround();plan()}
function meowl(x,y,t,moving){
    ctx.save();ctx.translate(x,y);const bob=moving?Math.sin(t*13)*1.5:Math.sin(t*2)*.6;ctx.translate(0,bob);ctx.lineWidth=1.6;ctx.strokeStyle='#171f1c';ctx.fillStyle='#d4d4ba';
    ctx.beginPath();ctx.moveTo(-10,2);ctx.bezierCurveTo(-17,-5,-12,-18,-10,-23);ctx.lineTo(-3,-18);ctx.quadraticCurveTo(0,-21,5,-18);ctx.lineTo(12,-23);ctx.quadraticCurveTo(17,-6,10,5);ctx.quadraticCurveTo(0,13,-10,2);ctx.fill();ctx.stroke();
    for(const sign of [-1,1]){ctx.beginPath();ctx.moveTo(sign*8,-8);ctx.quadraticCurveTo(sign*21,-1,sign*9,8);ctx.quadraticCurveTo(sign*6,2,sign*8,-8);ctx.fill();ctx.stroke();ctx.fillStyle='#e8e2c8';ctx.beginPath();ctx.ellipse(sign*5,-10,4.5,5.5,0,0,Math.PI*2);ctx.fill();ctx.fillStyle='#151c18';ctx.beginPath();ctx.ellipse(sign*5,-10,1.4,3,0,0,Math.PI*2);ctx.fill();ctx.fillStyle='#d4d4ba';ctx.beginPath();const foot=moving?Math.sin(t*13+sign)*2:0;ctx.moveTo(sign*5,8);ctx.lineTo(sign*8,12+foot);ctx.lineTo(sign*3,12+foot);ctx.stroke();}
    ctx.fillStyle='#b2975d';ctx.beginPath();ctx.moveTo(-2,-5);ctx.lineTo(2,-5);ctx.lineTo(0,-2);ctx.fill();
    ctx.fillStyle='#9c4d42';ctx.beginPath();ctx.moveTo(-14,-19);ctx.quadraticCurveTo(-14,-27,-2,-29);ctx.lineTo(12,-32);ctx.lineTo(9,-22);ctx.quadraticCurveTo(0,-19,-14,-19);ctx.fill();ctx.stroke();ctx.strokeStyle='#d8bd6b';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(-14,-20);ctx.quadraticCurveTo(0,-18,13,-22);ctx.stroke();ctx.restore();
}
function draw(t,moving){
    ctx.drawImage(ground,0,0);
    if($('#explored').checked){ctx.fillStyle='#9aa88b18';for(const [x,y] of result.visited)ctx.fillRect(x*S+2,y*S+2,8,8)}
    if(result.path.length){ctx.strokeStyle='#becaaa';ctx.lineWidth=2;ctx.setLineDash([5,6]);ctx.beginPath();result.path.forEach((p,i)=>{const q=center(p);i?ctx.lineTo(...q):ctx.moveTo(...q)});ctx.stroke();ctx.setLineDash([])}
    const [gx,gy]=center(goal);ctx.strokeStyle=result.path.length?'#ddc894':'#cf8376';ctx.lineWidth=2;ctx.beginPath();ctx.arc(gx,gy,8,0,Math.PI*2);ctx.moveTo(gx-13,gy);ctx.lineTo(gx+13,gy);ctx.moveTo(gx,gy-13);ctx.lineTo(gx,gy+13);ctx.stroke();meowl(...center(actor),t,moving);
}
function frame(time){
    const dt=Math.min(.04,(time-last)/1000||0);last=time;let moving=false;
    if(!document.hidden&&!paused&&result?.path.length&&index<result.path.length){let budget=dt*8;while(budget>0&&index<result.path.length){const target=result.path[index],dx=target[0]-actor[0],dy=target[1]-actor[1],distance=Math.hypot(dx,dy),step=Math.min(distance,budget);if(distance<.001){index++;continue}actor=[actor[0]+dx/distance*step,actor[1]+dy/distance*step];budget-=step;travel+=step;moving=true;if(step===distance)index++;}if(index===result.path.length)$('#status').textContent='Destination reached. Pick another, or redraw the terrain.';window.__botato.actor=[...actor]}
    if(!document.hidden&&(moving||drawDirty||!paused)){draw(time/1000,moving);drawDirty=false}requestAnimationFrame(frame);
}
function act(e){const rect=canvas.getBoundingClientRect(),x=Math.floor((e.clientX-rect.left)/rect.width*W),y=Math.floor((e.clientY-rect.top)/rect.height*H);if(x<1||y<1||x>=W-1||y>=H-1)return;
    if(tool==='goal'){goal=[x,y];plan();return}
    if(tool==='wall'&&Math.hypot(x-actor[0],y-actor[1])<5){$('#status').textContent='Leave a little room around the traveller before drawing.';return}
    paint(map.blocked,x,y,2,tool==='erase');updateGround();plan();
}
canvas.onpointerdown=e=>{canvas.setPointerCapture(e.pointerId);drag=true;act(e)};canvas.onpointermove=e=>{if(drag&&tool!=='goal')act(e)};canvas.onpointerup=()=>drag=false;canvas.onpointercancel=()=>drag=false;
canvas.onkeydown=e=>{const d={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[e.key];if(d){e.preventDefault();goal=[Math.max(1,Math.min(W-2,goal[0]+d[0])),Math.max(1,Math.min(H-2,goal[1]+d[1]))];plan()}else if(e.key==='Enter'){e.preventDefault();plan()}};
for(const b of document.querySelectorAll('[data-tool]'))b.onclick=()=>{tool=b.dataset.tool;for(const c of document.querySelectorAll('[data-tool]'))c.setAttribute('aria-pressed',c===b)};
$('#map').onchange=reset;$('#reset').onclick=reset;$('#clearance').oninput=plan;$('#explored').onchange=()=>drawDirty=true;$('#pause').onclick=()=>{paused=!paused;$('#pause').textContent=paused?'resume walk':'pause walk';drawDirty=true};
$('#export').onclick=()=>{const output={terrain:map.name,width:W,height:H,blocked:[...map.blocked],start:result.raw[0]||actor,goal,path:result.path,raw:result.raw,expanded:result.visited.length,cost:result.cost,clearance:Number($('#clearance').value)};const url=URL.createObjectURL(new Blob([JSON.stringify(output,null,2)],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='botato-route.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)};
reset();requestAnimationFrame(frame);

// The host binds these stable names to a locally verified official profile.
// No installed Codex version or hashed upstream filename leaks into this shell.
import * as original from './compat.js';
const shared=original,mascot={n:original.OfficialBadge,r:original.InitBadge};
const boot=window.__grassFishBoot;
if(!boot?.nonce||!window.chrome?.webview)throw Error('The native task view requires its bounded desktop host.');
shared.InitIntl();shared.InitScope();shared.InitPersistedAtoms();shared.SetPersistedAtoms({},()=>{});mascot.r();
const R=original.OfficialReact(),h=R.createElement,swipe=new original.OfficialSwipeInput();
const messages={
 'avatarOverlay.notificationFollowUpPlaceholder':'继续跟进','avatarOverlay.notificationFollowUpInput':'跟进 {title}',
 'avatarOverlay.notificationFollowUpError':'无法发送回复','avatarOverlay.replyNotification':'回复 {title}',
 'avatarOverlay.stopNotification':'停止 {title}','avatarOverlay.statusRunning':'正在运行','avatarOverlay.statusRunningSubtitle':'正在思考',
 'avatarOverlay.statusWaiting':'需要输入','avatarOverlay.statusReview':'已完成','avatarOverlay.statusFailed':'出错了',
 'avatarOverlay.dismissNotification':'关闭 {title}','avatarOverlay.notificationList':'任务通知',
 'avatarOverlay.expandNotificationStack':'展开任务列表，共 {count} 个任务','avatarOverlay.expandNotification':'展开 {title}',
 'avatarOverlay.collapseNotification':'收起 {title}','avatarOverlay.openNotification':'在 Codex 中打开',
};
let api,latest=boot.snapshot||{connected:false,detail:'正在连接 Codex…',items:[],expanded:false,layout:null},measureFrame=0;
const drafts=new Map((boot.state?.drafts||[]).map(d=>[d.id,d.text])),inflight=new Set(boot.state?.pending||[]),seenResults=new Map(),failures=new Map();
function reconcileDelivery(delivery){
 if(!delivery)return;
 inflight.clear();for(const key of delivery.pending||[])inflight.add(key);
 for(const result of delivery.results||[]){const key=result.id+':'+result.turnKey;if((seenResults.get(key)||0)>=result.version)continue;seenResults.set(key,result.version);api?.result(result);}
}
function send(action,extra={}){window.chrome.webview.postMessage(JSON.stringify({nonce:boot.nonce,action,...extra}));}
function measure(){if(measureFrame)return;measureFrame=requestAnimationFrame(()=>{measureFrame=0;const content=document.getElementById('native-content');if(content)send('measure',{height:Math.max(56,content.scrollHeight)});});}
function decorate(){
 for(const button of document.querySelectorAll('button[aria-label^="停止 "]')){
  button.disabled=true;button.setAttribute('aria-disabled','true');button.dataset.nativeStopUnavailable='true';
  button.title='当前连接不支持直接停止；请点击任务标题，在 Codex 原对话中停止。';
 }
 if(latest.layout){const arrow=document.querySelector('[data-testid="avatar-overlay-notification-badge"] svg');if(arrow)arrow.classList.toggle('rotate-180',!latest.layout.above);}
}
class Boundary extends R.Component {
 constructor(p){super(p);this.state={error:null};}static getDerivedStateFromError(error){return{error};}
 componentDidCatch(error){send('view-error',{error:String(error).slice(0,300)});}
 render(){return this.state.error?h('div',{className:'native-empty'},'任务浮层暂不可用；草鱼和本地提醒仍可继续使用。'):this.props.children;}
}
function App(){
 const [snapshot,setSnapshot]=R.useState(latest),[follow,setFollow]=R.useState(null),[stack,setStack]=R.useState(false),[notice,setNotice]=R.useState('');
 R.useEffect(()=>{send('ready');},[]);
 api={exportDraft(){const input=document.querySelector('textarea');if(follow&&input)drafts.set(follow.notificationId,input.value);},update(value){latest=value;reconcileDelivery(value.delivery);setSnapshot(value);},expand(){send('expand',{expanded:true});},openResult(result){setNotice(result.ok?'':result.error||'未能打开 Codex 对话，请稍后再试。');},result(result){const key=result.id+':'+result.turnKey;inflight.delete(key);
   if(result.ok){failures.delete(key);if((drafts.get(result.id)||'').trim()===(result.prompt||'').trim())drafts.set(result.id,'');}else failures.set(key,result.error||'发送未成功，草稿保留，不会自动重发。');
   setFollow(old=>{
   if(!old||old.notificationId!==result.id||old.turnKey!==result.turnKey)return old;
   if(result.ok){setNotice('');send('focus',{active:false});return null;}
   setNotice(result.error||'发送未成功，草稿保留，不会自动重发。');return{...old,submissionStatus:'error'};
 });}};
 R.useEffect(()=>{reconcileDelivery(snapshot.delivery);},[snapshot.delivery]);
 const notifications=snapshot.items.map(item=>({id:item.id,title:item.title,body:item.body,kind:'session',turnKey:item.turnKey,isLoading:item.isLoading,level:item.level,notificationPreferenceId:null,localConversationId:null,controlTarget:item.canReply?{type:'app-server-conversation',conversationId:item.id}:null}));
 // The official CSS fallback allocates three native-surface IDs per stack. Chunk only when fully
 // expanded, keeping every row rendered by the original stack/pill components rather than truncating.
 const groups=stack?Array.from({length:Math.ceil(notifications.length/3)},(_,i)=>notifications.slice(i*3,i*3+3)):[notifications];
 R.useEffect(()=>{if(follow&&!snapshot.items.some(n=>n.id===follow.notificationId&&n.turnKey===follow.turnKey)){setFollow(null);send('focus',{active:false});}},[snapshot.items,follow]);
 R.useEffect(()=>{if(!snapshot.expanded){setFollow(null);setNotice('');}},[snapshot.expanded]);
 R.useEffect(()=>{decorate();measure();if(follow?.submissionStatus==='error'){const alert=document.querySelector('[role="alert"]');if(alert)alert.title=notice;}},[snapshot,follow,notice,stack]);
 R.useEffect(()=>{if(follow&&snapshot.expanded){send('focus',{active:true});requestAnimationFrame(()=>{const input=document.querySelector('textarea');if(input&&follow.draft&&input.value!==follow.draft){Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(input,follow.draft);input.dispatchEvent(new Event('input',{bubbles:true}));}input?.focus();});}},[follow?.notificationId,snapshot.expanded]);
 R.useEffect(()=>{const content=document.getElementById('native-content');if(!content)return;const observer=new ResizeObserver(measure);observer.observe(content);return()=>observer.disconnect();},[snapshot.expanded]);
 function activate(id){
  if(notifications.length>1&&!stack){setStack(true);return;}
  const n=notifications.find(x=>x.id===id);if(!n)return;
  if(!snapshot.connected){setNotice('Codex 连接暂不可用，无法打开原对话。');return;}
  setNotice('');send('open',{id:n.id,turnKey:n.turnKey});
 }
 function control(notification,action){const id=notification.id,turnKey=notification.turnKey,key=id+':'+turnKey;
  if(action.type==='open-follow-up'){setNotice(failures.get(key)||'');setFollow({notificationId:id,turnKey,draft:drafts.get(id)||'',submissionStatus:[...inflight].some(x=>x.startsWith(id+':'))?'submitting':failures.has(key)?'error':'idle'});}
  else if(action.type==='close-follow-up'){setFollow(null);send('focus',{active:false});}
  else if(action.type==='follow-up-editor-changed'){drafts.set(id,action.draft);send('draft',{id,turnKey,draft:action.draft});setFollow(old=>old?{...old,draft:action.draft}:old);}
  else if(action.type==='submit-follow-up'){
   if(!snapshot.connected||[...inflight].some(x=>x.startsWith(id+':'))||!snapshot.items.some(n=>n.id===id&&n.turnKey===turnKey&&n.canReply))return;
   const prompt=action.prompt.trim();if(!prompt||prompt.length>20000){setNotice('消息请填写1～20000个字符。');return;}
   inflight.add(key);drafts.set(id,prompt);setFollow(old=>({...old,submissionStatus:'submitting'}));send('send',{id,turnKey,prompt});
  }else if(action.type==='stop'){setNotice('当前连接没有停止接口，请在 Codex 原对话中停止。');}
 }
 const layout=snapshot.layout;if(!layout)return null;
 const badge={ariaLabel:snapshot.expanded?'收起任务':snapshot.connected?'显示任务，共 '+notifications.length+' 个':'Codex 连接暂不可用',backgroundColor:'var(--color-background-info-soft)',foregroundColor:'var(--color-text)',content:snapshot.expanded?h(shared.OfficialChevron,{className:'icon-sm'+(layout.above?'':' rotate-180')}):snapshot.connected?(notifications.length||'·'):'…',isGlassy:true,isIconOnly:snapshot.expanded,onClick:()=>{send('expand',{expanded:!snapshot.expanded});if(snapshot.expanded){send('focus',{active:false});setFollow(null);}}};
 const info=!snapshot.connected?snapshot.detail:!notifications.length?'当前没有任务通知。':'仅显示当前任务及待查看通知';
 return h(R.Fragment,{},h('div',{className:'native-anchor',style:{inset:0}},h(mascot.n,{key:String(layout.badgeX)+":"+String(layout.badgeY),notificationBadge:badge,presentationPosition:{left:layout.badgeX,top:layout.badgeY,width:24,height:24},nativeCornerRadius:12})),snapshot.expanded?h('div',{className:'native-tray',style:{left:layout.trayX,top:layout.trayY,width:layout.trayWidth}},h('div',{className:'native-scroll',style:{maxHeight:layout.maxHeight}},h('div',{id:'native-content'},notifications.length?h("div",{className:"native-groups"},groups.map(group=>h(original.OfficialActivityStack,{key:group[0].id,notifications:group,notificationFollowUp:follow,activitySwipeInput:swipe,animatesActivityLayout:true,isNotificationStackCollapsed:false,isExpanded:stack,isVisiblyExpanded:stack,expandedNotificationIds:[],enteringNotificationIds:[],retainedForegroundNotificationIds:[],coveredForegroundNotificationIds:[],hiddenStartCoveredNotificationIds:[],onActivateNotification:activate,onRunNotificationControl:control,onDismissNotification:n=>{send('dismiss',{id:n.id,turnKey:n.turnKey});if(follow?.notificationId===n.id){setFollow(null);send('focus',{active:false});}},onRunNotificationAction:()=>{}}))):h('div',{className:'native-empty'},info),notice?h('div',{className:'native-note',role:'status'},notice):null))):null);
}
shared.OfficialReactDom().createRoot(document.getElementById('root')).render(h(Boundary,{},h(shared.OfficialIntlProvider,{locale:'zh-CN',messages,onError:()=>{}},h(shared.OfficialScopeProvider,{scope:shared.OfficialAppScope,value:{}},h(App)))));
new ResizeObserver(measure).observe(document.getElementById('root'));
new MutationObserver(()=>{decorate();measure();}).observe(document.getElementById('root'),{subtree:true,childList:true});
window.grassFish={update(value){if(api)api.update(value);else latest=value;},exportState(){api?.exportDraft();return{drafts:[...drafts].map(([id,text])=>({id,text}))};},result(value){api?.result(value);},openResult(value){api?.openResult(value);},focus(){document.querySelector('textarea')?.focus();},qa(){return{hasReply:!!document.querySelector('textarea'),disabledStops:[...document.querySelectorAll('[data-native-stop-unavailable]')].every(x=>x.disabled),rows:document.querySelectorAll('[data-avatar-overlay-hit-region="activity-pill"]').length};}};

import { defineCatalog } from '@json-render/core';
import { schema } from '@json-render/react/schema';
import { z } from 'zod';

export const sourceCommit = '3ad381881194e7011ad3ccd6d668033495a06c29';
export const catalog = defineCatalog(schema, {
  components: {
    Board: { props: z.object({ title: z.string(), subtitle: z.string() }), slots: ['default'] },
    Metric: { props: z.object({ label: z.string(), value: z.string(), detail: z.string(), tone: z.enum(['mint','amber','blue']) }) },
    Trend: { props: z.object({ title: z.string(), values: z.array(z.number()), labels: z.array(z.string()), unit: z.string() }) },
    Queue: { props: z.object({ title: z.string(), rows: z.array(z.object({ name: z.string(), detail: z.string(), status: z.string() })) }) },
    Alert: { props: z.object({ title: z.string(), body: z.string(), level: z.enum(['info','warning']) }) },
    Reviewers: { props: z.object({ title: z.string(), names: z.array(z.string()) }) },
    Checklist: { props: z.object({ title: z.string(), items: z.array(z.string()) }) },
  },
  actions: {},
});
const card = (id, description, type, props, extra = {}) => ({id, description, root: false, element: {type, props}, ...extra});
const board = (title, subtitle) => ({id:'board', description:'The only root dashboard container. Include this as the root and put all other elements directly inside it.', root:true, element:{type:'Board', props:{title, subtitle}}});
const metric = (id,label,value,detail,tone) => card(id, `${label}: ${value}. ${detail}`, 'Metric', {label,value,detail,tone});
export const scenarios = [
  {
    id:'review', number:'01', label:'PR review', title:'El diff marca el equipo.', tag:'ENGINEERING', color:'mint',
    description:'Una PR toca permisos y acceso a datos. Jev decide qué piezas necesita el panel de revisión.',
    context:{dataKind:'synthetic demo', changes:['Authorization middleware changed','Database query added inside a loop','No regression tests added']},
    prompt:'Compose a PR review dashboard. Put the security alert first, then specialist reviewers, then the changed-files queue, then the required-checks checklist. Include the risk and changed-files metrics. Exclude latency trends and the documentation-only notice. Use Board as root.',
    candidates:[board('Revisión en foco','PR #142 · Autorización y consulta de reservas'),
      metric('risk','Riesgo detectado','ALTO','Señales sintéticas de permisos y rendimiento','amber'),
      metric('files','Archivos modificados','07','3 áreas del producto afectadas','mint'),
      card('security','Security alert: changes to authorization checks and data access boundaries.','Alert',{title:'Permisos bajo revisión',body:'El diff de ejemplo cambia la validación de acceso a reservas. Revisar aislamiento entre clientes antes de integrar.',level:'warning'}),
      card('reviewers','Specialist reviewer panel: quality, security, performance and test strategy.','Reviewers',{title:'Equipo de revisión',names:['Calidad','Seguridad','Rendimiento','Estrategia de tests']}),
      card('files-queue','Changed-files queue with authorization middleware, repository query and test coverage.','Queue',{title:'Mapa del cambio',rows:[{name:'Authorization middleware',detail:'Validación de acceso',status:'Sensible'},{name:'Booking repository',detail:'Consulta dentro de bucle',status:'Revisar'},{name:'Regression suite',detail:'Sin casos nuevos',status:'Pendiente'}]}),
      card('checks','Required-checks checklist for isolation, query batching and regression coverage.','Checklist',{title:'Antes del merge',items:['Verificar aislamiento entre clientes','Medir número de consultas por reserva','Añadir casos de regresión']}),
      card('latency','Unrelated latency trend. Only include if the user explicitly requests latency.','Trend',{title:'Latencia histórica',values:[120,160,140,180,130,125,135],labels:['L','M','X','J','V','S','D'],unit:'ms'}),
      card('docs','Documentation-only notice, irrelevant for a code change.','Alert',{title:'Solo documentación',body:'No se han cambiado rutas de ejecución.',level:'info'})],
    edits:[{id:'focused',label:'Solo alerta y revisores',prompt:'Remove all components except the root Board, the security Alert and the Reviewers panel. Keep the alert before the reviewers.'}],
  },
  {
    id:'ops',number:'02',label:'Incident room',title:'Menos ruido. Más señal.',tag:'OPERATIONS',color:'amber',
    description:'Una incidencia de reservas pide un panel distinto: estado, latencia y acciones inmediatas.',
    context:{dataKind:'synthetic demo',incident:'Booking API elevated errors after a deployment',errorRate:'4.8%',p95:'840 ms'},
    prompt:'Compose an incident dashboard with the incident Alert first, then the error-rate and p95 metrics, then the latency Trend, followed by the affected-services Queue and the response Checklist. Do not include revenue or reviewer components.',
    candidates:[board('Sala de incidencia','INC-028 · Booking API · Datos de demostración'),
      card('incident','Incident alert: Booking API elevated errors following deployment.','Alert',{title:'Degradación en reservas',body:'Escenario sintético: aumento de errores tras el despliegue. El panel prioriza impacto y diagnóstico.',level:'warning'}),
      metric('errors','Tasa de error','4,8%','Objetivo del escenario: < 1%','amber'),
      metric('p95','Latencia p95','840 ms','+620 ms respecto a la referencia','blue'),
      card('trend','Latency trend showing increase following deployment, in milliseconds.','Trend',{title:'Latencia · últimos 30 min',values:[210,230,220,490,760,920,840],labels:['−30','−25','−20','−15','−10','−5','Ahora'],unit:'ms'}),
      card('services','Affected services queue with Booking API, inventory and payments.','Queue',{title:'Servicios afectados',rows:[{name:'Booking API',detail:'Errores después del deploy',status:'Degradado'},{name:'Inventory',detail:'Dependencia lenta',status:'Investigar'},{name:'Payments',detail:'Sin impacto observado',status:'Estable'}]}),
      card('response','Response checklist: compare deployment, inspect traces, evaluate rollback.','Checklist',{title:'Próximos pasos',items:['Comparar cambios del último despliegue','Inspeccionar trazas de llamadas fallidas','Evaluar rollback con el responsable']}),
      metric('revenue','Ingresos','€84.200','Métrica comercial no relacionada','mint'),
      card('reviewers','Code-review specialists; unrelated to this incident dashboard.','Reviewers',{title:'Code reviewers',names:['Arquitectura','Calidad']})],
    edits:[{id:'briefing',label:'Briefing sin checklist',prompt:'Remove the response Checklist and the affected-services Queue. Keep the incident Alert, the error and latency metrics, and the latency Trend.'}],
  },
  {
    id:'commerce',number:'03',label:'Revenue room',title:'El negocio, a primera vista.',tag:'BUSINESS',color:'blue',
    description:'La misma mecánica construye un resumen comercial con métricas y pedidos, sin inventar cifras.',
    context:{dataKind:'synthetic demo',purpose:'Weekly commerce summary for a fictional travel shop'},
    prompt:'Compose a weekly sales dashboard. Place the orders Queue first, then revenue and bookings Metric cards, then the weekly revenue Trend and the business summary Alert. Exclude security alerts and response checklists.',
    candidates:[board('Pulso comercial','Semana 38 · Travel shop ficticia'),
      metric('revenue','Ingresos semanales','€84.200','+12,4% en este dataset sintético','mint'),
      metric('bookings','Reservas','326','Estancia media: 3,2 noches','blue'),
      card('orders','Recent orders queue: three fictional hotel bookings.','Queue',{title:'Últimas reservas',rows:[{name:'Palma · 3 noches',detail:'Reserva DEMO-103',status:'Confirmada'},{name:'Madrid · 2 noches',detail:'Reserva DEMO-102',status:'Confirmada'},{name:'Lisboa · 4 noches',detail:'Reserva DEMO-101',status:'Pendiente'}]}),
      card('sales','Weekly revenue trend, synthetic values in thousands of euros.','Trend',{title:'Ingresos por día',values:[8,12,9,15,11,17,12.2],labels:['L','M','X','J','V','S','D'],unit:'k€'}),
      card('summary','Business summary notice describing fictional weekend growth.','Alert',{title:'El fin de semana impulsa la semana',body:'Lectura preparada del dataset: el sábado concentra el mayor volumen. Jev selecciona esta pieza; no ha redactado este texto.',level:'info'}),
      card('security','Security incident alert; unrelated to a commercial summary.','Alert',{title:'Alerta de seguridad',body:'Panel reservado a incidencias de acceso.',level:'warning'}),
      card('response','Technical incident-response checklist, irrelevant to sales.','Checklist',{title:'Diagnóstico técnico',items:['Revisar trazas','Comparar despliegues']})],
    edits:[{id:'executive',label:'Vista ejecutiva',prompt:'Remove the orders Queue. Keep both revenue and bookings Metric cards, the weekly revenue Trend and the business summary Alert.'}],
  },
];

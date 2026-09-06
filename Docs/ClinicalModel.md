# Modelo Clínico — Fase 1 (Vertical Slice: Sepse / Choque Séptico)

> **Revisão 2 — Calibração técnica.** Este documento foi atualizado após uma
> passada de calibração focada em remover tetos artificiais (clamps fazendo o
> papel de dinâmica fisiológica), corrigir a resposta exagerada à noradrenalina
> e tornar a integração numérica independente do tamanho do passo de tempo. A
> seção **"Log de calibração (Revisão 2)"** no fim do documento resume exatamente
> o que mudou e por quê. Nada em `Assets/Scripts/Clinical` ou
> `Assets/Scripts/ClinicalBridge` mudou de arquitetura — apenas as fórmulas
> dentro de `ClinicalFormulas.cs` e o critério de óbito em `PatientState.cs`.
>
> **Revisão 3 — Validação fisiológica (bateria de cenários), nenhum código
> alterado.** Esta revisão **não mudou nenhuma fórmula ou constante** — rodou
> uma bateria padronizada de 15 cenários (ver seção **"Validação fisiológica —
> bateria de cenários"**) e documentou o que foi observado, incluindo 5
> comportamentos potencialmente implausíveis (P1-P5) com o parâmetro/mecanismo
> exato responsável por cada um. As séries temporais completas estão em
> `Docs/ValidationData/*.csv`.
>
> **Revisão 4 — Correção de P1-P5.** Ver seção **"Revisão 4 — Correção dos
> achados P1-P5"**. Resumo: PAD nunca mais fica negativa (agora é uma fração
> da própria PAM, não um valor independente); uma variável explícita
> `EffectivePerfusion` (fluxo × autorregulação de pressão) foi introduzida e
> agora governa lactato/diurese/consciência em vez de PAM pura — confirmado
> por teste que a mesma PAM com fluxos diferentes produz lactato diferente;
> a dose de noradrenalina agora é literalmente mcg/kg/min com ED50/Hill
> recalibrados (0,05 mcg/kg/min deixou de ser quase inerte); a FR usa uma
> curva saturável em vez de um teto rígido; a consciência agora tem um score
> contínuo com inércia (τ=30min) em vez de trocar de estado instantaneamente.
> Nenhuma outra fórmula foi tocada. 77 testes automatizados (era 69), todos
> passando. Séries temporais "depois" em `Docs/ValidationData/Revision4/*.csv`,
> comparadas ponto a ponto com as de antes na nova seção.

Este documento descreve as fórmulas, parâmetros e simplificações usadas no núcleo
de simulação clínica implementado em `Assets/Scripts/Clinical/`. O objetivo desta
fase é ter um modelo **plausível na direção e na interação das variáveis**, não um
simulador médico validado. Tudo aqui deve ser revisado por alguém com formação
clínica antes de qualquer uso educacional real — ver a checklist final.

## Arquitetura (inalterada nesta revisão)

- `Assets/Scripts/Clinical/` — domínio clínico puro. **Zero dependência de
  UnityEngine** (garantido em tempo de compilação pelo `Clinical.asmdef`, que tem
  `"noEngineReferences": true`).
  - `Core/`: `VitalSigns`, `ClinicalClock`, `PatientState`, `ClinicalFormulas`.
  - `Events/`: `ClinicalEvent` (+ subtipos) e `ClinicalEventBus`.
  - `Diseases/`: `DiseaseProcess` (abstrato) e `SepsisDiseaseProcess`.
  - `Treatments/`: `TreatmentEffect` (abstrato), `CrystalloidBolusEffect`,
    `OxygenTherapyEffect`, `NorepinephrineInfusionEffect`, `AntibioticEffect`.
  - `PatientSimulationController`: orquestra clock + doenças + tratamentos + eventos.
- `Assets/Scripts/ClinicalBridge/` — adaptador Unity. **Não referencia
  `ClinicalFormulas` em nenhum ponto** (confirmado por busca no código) — só
  chama a API pública de `PatientSimulationController`, o que é exatamente o
  isolamento que permitiu recalibrar todas as fórmulas nesta revisão sem tocar
  em `PatientSimulationBehaviour` nem em `ClinicalDebugUI`.
- `Assets/Tests/EditMode/` — 48 testes NUnit (era 27 antes desta revisão),
  todos passando, fora do Play Mode.

### Ordem de atualização por tick (`PatientSimulationController.Tick`) — inalterada

1. Tratamentos aplicam suas entradas (infundem volume, atualizam nível de O₂ e
   dose de noradrenalina, aplicam morte bacteriana do antibiótico).
2. O processo de doença evolui (crescimento da infecção, leak capilar).
3. `PatientState.RecalculateVitals` deriva os sinais vitais a partir dos
   drivers internos resultantes.
4. Eventos são publicados e o óbito é checado (agora com **dois** critérios
   independentes — ver seção de óbito abaixo).

## Unidades e convenções (inalteradas)

- Tempo interno em **minutos simulados**; fórmulas usam **horas**.
- Variáveis internas "relativas" (RVS, volume) usam **1.0 = normal**.
- `PatientState.WeightKg` (padrão 70 kg) escala a dose de cristaloide.

## Linha de base — inalterada

| Variável | Valor |
|---|---|
| FC | 75 bpm | PA | 120/80 mmHg | PAM | 93 mmHg | FR | 16 irpm | SpO₂ | 98% |
| Temperatura | 37,0 °C | Consciência | Alerta | Diurese | 70 mL/h | Lactato | 1,0 mmol/L |

## Infecção (`InfectionBurden`, 0–1+) — inalterada

```
crescimento/h = 0.20 * I * (1 - I / 1.0)
morte/h       = 0.55 * nívelEfetivoAntibiótico * I
dI = (crescimento - morte) * deltaHoras
```

Crescimento e morte continuam sendo somados como dois deltas discretos por
tick (crescimento vem de `SepsisDiseaseProcess`, morte vem de
`AntibioticEffect` — dois objetos independentes, por arquitetura). Isso
permanece Euler explícito e **não** foi convertido para solução fechada,
porque uma solução fechada exigiria resolver as duas EDOs juntas, o que
significaria fundir `DiseaseProcess` e `TreatmentEffect` — fora do escopo
desta calibração (arquitetura inalterada). Na taxa e nos passos de tempo
usados aqui (`taxa*dt` sempre ≪ 1), Euler explícito é numericamente estável
para um mapa logístico, então isso não é um risco de instabilidade na prática.

## Antibiótico — **curva de efeito revisada** (agora exponencial suave)

**Antes:** rampa linear até 0,5h, platô até 6h, decaimento linear até 8h (dois
"cotovelos" na curva, e o efeito no minuto 1 já era proporcionalmente grande).

**Agora:**

```
onset/platô (0 → 6h):  nível = 1 - exp(-t / 0.6h)
declínio (após 6h):    nível = nível(6h) * exp(-(t-6h) / 1.2h)
```

- Curva suave (sem cotovelos), continua indo a 0 exatamente em t=0.
- **Não produz melhora imediata**: em t=5min, nível ≈ 0,13 (antes seria maior
  numa rampa linear de 30 min); em t=1h, ≈ 0,81; passa de 0,98 só perto de 6h.
- Ainda força redosagem: cai para ~19% de efeito ~2h após o fim da cobertura
  (6h) se não houver nova dose.
- **Teste adicionado**: precoce (t=0h) vs. tardio (t=10h) no mesmo paciente,
  comparados no mesmo instante absoluto (t=16h) — precoce chega com
  PAM≈88/lactato≈0,5/infecção≈0,028; tardio chega com PAM≈69/lactato≈2,56(!)
  /infecção≈0,037 — ou seja, dar o antibiótico tarde deixa o paciente à beira
  do choque séptico mesmo já tendo recebido a mesma droga.

## Volume intravascular (`IntravascularVolume`) — **integração corrigida**

Fórmula inalterada (`leak/h = 0.15 * I * V`), mas agora integrada com a
**solução fechada exata** (`V(t) = V0 * exp(-0.15*I*t)`) em vez de um passo de
Euler explícito. Antes, o resultado dependia sutilmente de como o tempo real
era fatiado em ticks (ex.: velocidade x1 vs. x20 no painel de debug podiam
divergir ligeiramente); agora dá exatamente o mesmo resultado independente do
tamanho do passo — verificado por teste (`StepVolume_IsStepSizeIndependent`).

`CrystalloidVolumeEffect` (mL → volume relativo) está **inalterada** — a
quantidade física de fluido infundido continua proporcional ao volume dado;
o que mudou foi como esse volume se traduz em benefício de PAM (ver
`VolumeFactor` abaixo).

## Hemodinâmica — **PAM não depende mais de um teto artificial**

### VolumeFactor — agora uma curva suave, sem clamp abrupto

**Antes:** `clamp(0.5 + 0.5*volume, 0.5, 1.15)` — piso e teto abruptos.

**Agora:**
```
volume <= 1.0:  VolumeFactor = 0.15 + 0.85*volume        (0 → 0,15 quando volume → 0)
volume  > 1.0:  VolumeFactor = 1 + 0.15*(1 - exp(-2*(volume-1)))   (satura suavemente perto de 1,15)
```
- **Consequência importante**: hipovolemia grave agora pode contribuir para
  uma PAM criticamente baixa (antes o piso de 0,5 impedia isso — uma
  simplificação que tornava a exsanguinação/hipovolemia extrema
  "artificialmente segura"). Teste: `Map_SevereHypovolemia_...` confirma PAM
  < 40 mmHg com RVS normal e volume no piso.
- Acima da normovolemia, o ganho continua saturando suavemente (sem cotovelo
  em 1,15 fixo) — 2 L vs. 10 L de cristaloide no mesmo paciente (t=4h) geram
  PAM 89,8 vs. 97,3 mmHg: mais volume ainda ajuda um pouco, mas 5× o volume
  não dá nem perto de 5× o efeito.

### AfterloadFactor — **novo**, é o que substitui o clamp de 150 mmHg

Antes, `PAM = 93 * RVS * VolumeFactor` era limitada só por
`clamp(..., 5, 150)` — um teto que fazia todo o trabalho de "impedir hipertensão
absurda" sozinho. Agora existe uma variável hemodinâmica real fazendo esse
papel: débito cardíaco efetivo cai quando a pós-carga (RVS) sobe muito acima do
normal (mismatch de pós-carga, física cardiovascular básica), e sobe
levemente quando a RVS cai abaixo do normal (padrão de "choque quente"/alto
débito, comum na sepse inicial):

```
RVS > 1.0:  AfterloadFactor = 1 / (1 + 0.6*(RVS-1))
RVS <= 1.0: AfterloadFactor = 1 + 0.15*(1-RVS)

PAM = clamp(93 * RVS * VolumeFactor(Volume) * AfterloadFactor(RVS), 5, 180)
```

O `clamp(5, 180)` que sobra é uma **rede de segurança numérica muito larga**
(nunca deveria ser atingida nos cenários normais) — não é mais o mecanismo que
limita a resposta à noradrenalina. Isso é o que o pedido de calibração chamou
de "limite fisiológico derivado da hemodinâmica, não simplesmente clamp de
PAM".

## Noradrenalina — **revisão completa** (o item central desta calibração)

### O problema identificado

Na versão anterior, `NorepinephrineEffect(dose) = 0.9 * dose/(dose+0.3)`
somava até **+0,9** na RVS-alvo **independente de quão vasoplégico o paciente
já estava**. Num paciente com sepse leve (RVS quase normal), uma dose de 0,5
levava a RVS-alvo a ~1,9 e a PAM batia direto no teto artificial de 150 mmHg.
Não havia (a) dependência do grau de vasoplegia, (b) resposta marginal
decrescente clara em doses altas, nem (c) qualquer efeito adverso associado a
doses excessivas.

### O modelo novo

```
Responsividade(I) = clamp(1 - 0.5*I, 0.5, 1)              // resistência a catecolaminas em choque grave
Hill(dose)         = dose^1.6 / (dose^1.6 + 0.3^1.6)       // curva sigmoide, satura suavemente
EfeitoNoradrenalina(dose, I) = 0.6 * Responsividade(I) * Hill(dose)

RVS-alvo = clamp(1 - 0.80*I + EfeitoNoradrenalina(dose, I), 0.15, 2.5)
```

- **Emax reduzido de 0,9 para 0,6**: representa o efeito farmacológico máximo
  possível (um parâmetro real de PK/PD, não um clamp arbitrário) — mesmo dose
  "infinita" não soma mais que 0,6 de RVS relativa.
- **Coeficiente de Hill 1,6** (em vez de uma Emax pura): curva sigmoide — pouco
  efeito muito abaixo de ED50, resposta mais acentuada perto de ED50 (0,3), e
  ganho marginal decrescente claro acima disso. Confirmado por teste:
  incremento de dose 0,2→0,5 rende mais PAM que o incremento 1,0→1,3.
- **Responsividade dependente da vasoplegia**: quanto maior a carga infecciosa
  (mais vasoplégico o paciente), menor a fração do efeito máximo alcançável
  (piso de 50% de responsividade mesmo no choque mais grave — nunca zero,
  mas claramente **resposta diminuída**, refletindo a "resistência a
  catecolaminas" descrita em choque séptico refratário).
- **Efeito cronotrópico direto** (novo): `+5 bpm por unidade de dose`, somado
  linearmente (sem saturação) à FC, **independente** do termo reflexo
  (que cai conforme a PAM melhora). Isso faz a FC voltar a subir com doses
  crescentes mesmo depois que a PAM já melhorou — um sinal visível de
  "dose excessiva / risco de arritmia", que é o item pedido de "possibilidade
  de taquicardia/efeitos adversos em doses elevadas".
- **Limite fisiológico via AfterloadFactor** (ver seção acima) em vez de clamp
  de PAM.

### Resultados observados (valores reais, extraídos rodando o modelo)

| Cenário | Antes (dose 0,5, t≈4h) | Agora |
|---|---|---|
| Paciente pouco vasoplégico (I≈0,05–0,1), dose 1,0 | PAM ≈ 132 mmHg (quase no teto de 150) | PAM ≈ 106–108 mmHg, RVS ≈ 1,4–1,5 |
| Paciente em vasoplegia grave (I≈0,74), dose 1,0 | (não testado) | PAM 19,6→29,9 mmHg — melhora real, mas **permanece em choque** |
| Doses 0 → 0,3 → 0,6 → 1,5 (mesmo paciente, t=4h) | — | PAM 83,2 → 95,0 → 97,8 → 99,1 (ganhos de +11,8 / +2,8 / +1,3 — decrescente); FC 94 → 78 → 80 → 85 (sobe de novo com a dose, apesar da PAM estável) |

Isso satisfaz, com números concretos: (1) relação dose-resposta não linear,
(2) efeito dependente do grau de vasoplegia, (3) resposta marginal
decrescente, (4) sinal de efeito adverso em dose alta, (5) limite fisiológico
emergente em vez de clamp.

## Oxigenação — **SpO₂ revisada** (piso artificial removido, efeito de shunt adicionado)

**Antes:** `LungInjuryCoefficient=18` (dano pulmonar suave demais — SpO₂ nunca
caía abaixo de ~80% mesmo no pior cenário) e piso rígido em 70% (impedia
hipóxia realmente crítica, tornando insuficiência respiratória isolada
impossível como causa de óbito).

**Agora:**
```
penalidadeShunt = clamp(I, 0, 1) * 0.5
benefícioO2Efetivo = benefícioBruto(nível) * (1 - penalidadeShunt)
SpO2 = clamp(98 - 55*I + benefícioO2Efetivo, 35, 100)
  benefícioBruto: 0=ar ambiente, 1=cateter(+6), 2=máscara/AF(+14)
```

- `LungInjuryCoefficient` subiu de 18 para 55: agora sepse grave sem suporte de
  O₂ pode chegar a SpO₂≈43-58% (crítico) em vez de nunca passar de ~80%.
- **Novo**: o benefício do O₂ suplementar é **atenuado pela gravidade** (efeito
  shunt) — em lesão pulmonar leve, máscara/alto-fluxo ainda ajuda bastante; em
  lesão grave, o mesmo suporte ajuda proporcionalmente menos (fisiologia real
  de shunt intrapulmonar: FiO₂ alta não corrige shunt verdadeiro). Confirmado
  por teste comparando o ganho de SpO₂ do mesmo nível de O₂ em burden leve vs. grave.
- **Piso numérico** baixou de 70 para 35 (hipóxia crítica real), o que
  viabilizou o novo critério de óbito por insuficiência respiratória abaixo.

## Critérios de deterioração e óbito — **agora duas vias independentes**

**Antes:** só existia óbito por PAM < 15 mmHg sustentada por ≥ 2 min. Hipóxia
isolada nunca podia matar, porque o piso artificial de SpO₂ (70%) nunca deixava
a saturação cair a um nível realmente letal.

**Agora**, ambas checadas a cada tick (o que disparar primeiro define a causa):

```
PAM < 15 mmHg por ≥ 2 min       → "Choque refratário / colapso cardiovascular"
SpO2 < 45% por ≥ 5 min          → "Insuficiência respiratória refratária"
```

Isso é ainda uma simplificação (ver checklist final — sem PCR modelada, sem
falência de múltiplos órgãos gradual), mas remove a assimetria anterior em que
somente a via hemodinâmica podia ser fatal. Teste
`RespiratoryFailure_IsAPossibleCauseOfDeath_...` reproduz um paciente mantido
hemodinamicamente estável só com fluidos repetidos (sem antibiótico, sem O₂) e
confirma que ele morre mesmo assim — de hipóxia, não de choque.

## Revisão 4 — Correção dos achados P1-P5

Esta revisão corrige **exatamente** os 5 comportamentos implausíveis
documentados na validação da Revisão 3 (seção mais abaixo), sem adicionar
doenças, medicamentos ou features novas. Nenhuma outra fórmula foi tocada.
`Assets/Scripts/ClinicalBridge/` só recebeu uma mudança de texto (rótulo
"mcg/kg/min" e incrementos de 0,05 no lugar de 0,1 no painel de debug) — a
separação Clinical/ClinicalBridge e a ausência total de `UnityEngine` em
`Assets/Scripts/Clinical/` permanecem intactas (garantidas pelo
`Clinical.asmdef` com `noEngineReferences: true`, inalterado).

### P1 — Pressão de pulso agora é uma fração da PAM, não um valor independente

**Antes**: `PulsePressure(svr) = clamp(40*(2-svr), 15, 90)` — dependia só da
RVS. Em choque terminal (PAM baixíssima + RVS baixíssima), `PP/3` podia
exceder a própria PAM e `PAD = PAM - PP/3` ficava negativa.

**Agora**:
```
fraçãoPP(RVS) = clamp(0.43 * (2 - RVS), 0.15, 0.85)     // 0.43 = 40/93, a fração observada no basal
PP(PAM, RVS)  = PAM * fraçãoPP(RVS)
PAD = PAM - PP/3   =   PAM * (1 - fraçãoPP/3)
```
Como a fração está sempre entre 0,15 e 0,85 (bem abaixo de 3,0), `1 -
fraçãoPP/3` está sempre entre ~0,72 e ~0,95 — **PAD é matematicamente
positiva para qualquer PAM ≥ 0**, sem nenhum clamp direto sobre PAD. Isso é a
correção "fisiológica, não um clamp arbitrário" pedida: pressão de pulso
passou a ser entendida como proporção da própria pressão de perfusão
(modulada pelo tônus vascular/complacência arterial), não uma quantidade
absoluta independente.

**Evidência**: no cenário de sepse não tratada, PAD no óbito (PAM=14,9,
RVS=0,305) era **-6,9 mmHg** antes e é **+11,4 mmHg** depois — em toda a
bateria de 15 cenários (nenhum exceção), PAD nunca mais fica negativa. Testes:
`BloodPressure_AtCriticallyLowMapAndSvr_DiastolicNeverNegative`,
`BloodPressure_AcrossExtremeMapRange_DiastolicNeverNegative` (varre PAM 5-150
× RVS 0,15-2,5 inteira).

### P3 — Fluxo/perfusão efetiva explícita, separada da PAM

**Antes**: `PerfusionIndex(map) = clamp(map/93, 0, 1.3)` alimentava
lactato/diurese — perfusão tecidual dependia **só** de PAM. Um paciente com
PAM normalizada por vasoconstrição pura (sem volume) mostrava lactato
praticamente igual ao de um paciente com PAM normalizada por volume
adequado.

**Agora**, duas peças novas em `ClinicalFormulas.cs`:
```
FluxoEfetivo(Volume, RVS) = VolumeFactor(Volume) * AfterloadFactor(RVS)
   // reaproveita os MESMOS termos que já compunham a PAM (Revisão 2) -
   // PAM = PAM_basal * RVS * FluxoEfetivo é a identidade PAM ≈ DC × RVS

PortãoDeAutorregulação(PAM) = clamp(PAM / 65, 0, 1)
   // abaixo de ~65 mmHg, a autorregulação falha e a perfusão passa a
   // depender diretamente da pressão, mesmo com fluxo nominal adequado

EffectivePerfusion(PAM, Volume, RVS) = clamp(FluxoEfetivo * PortãoDeAutorregulação, 0, 1.3)
```
`EffectivePerfusion` (1,0 = normal) é agora **explícita e visível** —
`PatientState.EffectivePerfusion`, exibida no painel de debug — e substitui
o antigo `PerfusionIndex(map)` em `StepLactate` e em `UrineOutput`
(`UrineOutput` também passou a multiplicar pelo `FluxoEfetivo`, preservando o
valor basal de 92 mL/h já visto em toda a bateria, já que `FluxoEfetivo=1` na
normovolemia). A consciência (ver P5) também usa `EffectivePerfusion` em vez
de PAM crua.

**Evidência direta (o próprio teste-diagnóstico do achado P3, refeito)**: em
t=8h, paciente com 3000 mL de cristaloide + noradrenalina 0,3 (PAM=106,1) tem
Lactato=0,64 e Perfusão=0,890; paciente **sem nenhum fluido**, só com dose
mais alta de noradrenalina (0,85) para alcançar PAM parecida (92,6), tem
Lactato=1,12 e Perfusão=0,737 — **quase o dobro do lactato**, apesar de PAM
similar. Antes da correção esses dois pacientes mostravam lactato
praticamente idêntico (0,50 vs. 0,59). Testes:
`EffectivePerfusion_DiffersAtSameMap_WhenFlowDiffers`,
`StepLactate_SamePressureDifferentPerfusion_ProducesDifferentLactate`,
`EffectivePerfusion_GatedByAutoregulation_BelowThresholdMAP`,
`UrineOutput_ReducedByPoorFlow_EvenAtGoodMAP`.

*Limitação que permanece*: `EffectivePerfusion` ainda é uma função puramente
algébrica de PAM/Volume/RVS no instante atual (sem sua própria inércia) — o
que tem inércia agora é só a consciência (P5), que consome esse valor. Um
"fluxo" fisiológico real também teria alguma dinâmica própria; isso não foi
modelado, para não expandir demais o escopo desta correção pontual.

### P2 — Noradrenalina agora em mcg/kg/min reais, ED50/Hill recalibrados

**Antes**: `ED50=0,3`, `Hill=1,6` num "valor de dose" sem unidade definida.
A bateria de validação assumiu (por falta de alternativa) que esse número
correspondia a mcg/kg/min — e sob essa leitura, uma dose baixa real de
0,05 mcg/kg/min produzia só ~5% do efeito máximo (praticamente inerte).

**Agora**: o campo `NorepinephrineInfusionEffect.Dose` é **definido para
ser** mcg/kg/min (documentado no código); `ED50` e `Hill` foram recalibrados
para essa unidade. `Emax=0,6` **não mudou** — é o efeito farmacológico
máximo, uma propriedade do receptor/droga, independente da unidade de dose.

```
ED50 = 0.1 mcg/kg/min   (era 0,3, sem unidade definida)
Hill = 1.4              (era 1,6)
Hill(dose) = dose^1.4 / (dose^1.4 + 0.1^1.4)
```

| Dose (mcg/kg/min) | Hill(dose) — fração do Emax | Antes (ED50=0,3, Hill=1,6) |
|---|---|---|
| 0,05 | **27%** | ~5% |
| 0,10 | 50% (por definição, =ED50) | ~15% |
| 0,20 | 72% | ~34% |
| 0,50 | 90% | ~69% |
| 1,00 | 96% | ~87% |

A escolha de ED50=0,1 é uma estimativa de ordem de grandeza baseada nas
faixas de dose clínica típicas (início ~0,01-0,05, dose alta ~0,5-1,0
mcg/kg/min) — **não vem de um estudo de dose-resposta farmacológico
específico**; ainda é um parâmetro arbitrário sujeito a revisão médica (ver
checklist final). O que a recalibração garante é que a FORMA da curva
(sigmoide, saturante, com resistência a catecolaminas em choque grave -
inalterada) responde de um jeito clinicamente razoável às doses que um
jogador/clínico realmente usaria.

**Evidência**: no mesmo paciente (t=6h, PAM=77,9), dose 0,05 agora produz
PAM=86,9 em 15 min (Δ+9,0) — antes produzia Δ+1,4. Dose 1,0 continua próxima
da saturação (Δ+18,8, muito parecido com o Δ+17,7 de antes, como esperado já
que Emax não mudou). Testes:
`NorepinephrineEffect_AtLowClinicalDose_IsNoLongerNegligible`,
`NorepinephrineEffect_AtHighClinicalDose_NearlySaturates`,
`Norepinephrine_LowestClinicalDose_StillProducesVisibleEffect` (nível de
integração, via `PatientSimulationController`),
`Norepinephrine_AtClinicalMcgPerKgPerMinDoses_RaisesMapAboveUntreated`
(parametrizado nas 5 doses pedidas: 0,05/0,1/0,2/0,5/1,0).

### P4 — Frequência respiratória: resposta saturável em vez de teto rígido

**Antes**: `RR = clamp(16 + hipóxia + lactato + febre, 8, 50)` — a validação
mostrou esse teto de 50 sendo atingido **rotineiramente** (não como exceção)
em todo cenário que evoluía a choque grave.

**Agora**, o mesmo "drive" (soma de hipóxia+lactato+febre, ainda ilimitado)
passa por uma curva saturante em vez de ser cortado:
```
drive = 0.6*max(0,95-SpO2) + 3*max(0,Lactato-1) + 2*max(0,Temp-37)   // inalterado, ainda sem limite
incrementoSaturado = 34 * (1 - exp(-drive / 17))
FR = clamp(16 + incrementoSaturado, 8, 60)   // 60 = rail numérico largo, raramente relevante
```
O incremento máximo (34, dando assíntota 50 — o mesmo valor do teto antigo)
agora é **aproximado suavemente**, nunca atingido exatamente: mesmo em
drives extremos, FR fica em ~49-49,5, nunca trava numa parede. O ganho
marginal por unidade de drive adicional encolhe continuamente, o mesmo
princípio já usado para a PAM (Revisão 2) e para o fluxo/perfusão (P3
acima).

**Evidência**: no cenário de sepse não tratada, FR no óbito era exatamente
**50,0** antes e é **49,2** depois; em cenários com drive ainda mais extremo
(testado isoladamente, SpO2=35/lactato=50/temp=42), FR fica em ~49,5, nunca
50 ou mais. Testes: `RespiratoryRate_MarginalGain_ShrinksAtHigherDrive`,
`RespiratoryRate_AtExtremeDrive_ApproachesButNeverReachesHardCeiling`,
`RespiratoryDrive_IsUnboundedInput_ButRateStaysSaturated`.

### P5 — Consciência com inércia temporal (score contínuo + histerese)

**Antes**: `Consciousness(map, spo2)` recalculada do zero a cada tick com
limiares duros — o nível exibido podia pular instantaneamente de qualquer
estado para qualquer outro num único tick.

**Agora**, um score contínuo de mentação (0-1) com **memória**, usando o
mesmo padrão de relaxamento exponencial já validado para RVS/temperatura:
```
alvoScore(EffectivePerfusion, SpO2) = min(
    clamp(EffectivePerfusion, 0, 1),                    // componente de perfusão
    clamp((SpO2-70)/(98-70), 0, 1)                       // componente de oxigenação
)
// "min" em vez de exigir os dois simultaneamente por um AND rígido - o
// pior dos dois componentes limita a mentação, de forma contínua.

score(t) = alvoScore + (score(t-1) - alvoScore) * exp(-Δt / 0.5h)   // τ=30min, mesma forma de StepSvr/StepTemperature

Nível exibido = Alerta (score≥0,75) | Confuso (≥0,50) | Sonolento (≥0,25) | Não responsivo (<0,25)
```
`PatientState.ConsciousnessScore` é exposto (visível no painel de debug) além
do enum discreto em `VitalSigns.Consciousness`. τ=30min é um valor de
**pacing de jogo assumido**, não extraído de um estudo específico de
resolução de encefalopatia — arbitrário, documentado.

**Evidência**: no cenário de sepse não tratada, o paciente passava
instantaneamente de "Confuso" (t=6h) para "Não responsivo" (t=16h) sem
passar por "Sonolento" registrado nos checkpoints antes; agora passa por
Confuso (t=6-8h) → Sonolento (t=10-12h, score caindo de 0,489 a 0,325) → só
chega a Não responsivo por volta de t=14-16h (score≈0,006 em t=16h). No
cenário de antibiótico tardio, a recuperação também deixou de ser
instantânea: Sonolento (t=10h) → Confuso (t=12h) → Alerta (t=16h), em vez do
salto direto observado antes. Testes:
`ConsciousnessScore_DoesNotJumpInstantlyToTarget`,
`ConsciousnessScore_GraduallyApproachesTarget_OverMultipleTicks`,
`Consciousness_LevelFromScore_MatchesThresholds`,
`ConsciousnessScoreTarget_IsLimitedByWorseOfPerfusionOrOxygenation`,
`Consciousness_TransitionsGradually_NotInstantlyOnASingleTick` (integração),
`ConsciousnessScore_LagsBehindASuddenPerfusionCrash` (integração).

## Estabilidade numérica — **três integrações passaram a ser exatas**

`StepSvr`, `StepVolume` e `StepLactate` usavam um passo de Euler (RVS/Temp) ou
eram só parcialmente explícitas. Cada uma delas é, na verdade, uma EDO linear
de primeira ordem dentro de um tick (alvo/parâmetros tratados como constantes
durante o tick) que **tem solução fechada**:

- `StepSvr` / `StepTemperature`: relaxamento exponencial exato para o alvo —
  `X(t) = alvo + (X0-alvo)*exp(-t/tau)` — em vez de interpolação linear com
  taxa capada em 1 (que "saltava" direto pro alvo em qualquer tick ≥ tau).
- `StepVolume`: decaimento exponencial exato — `V(t) = V0*exp(-k*I*t)`.
- `StepLactate`: solução exata de produção/clearança lineares —
  `L(t) = L_ss + (L0-L_ss)*exp(-λt)`, com `L_ss = produção/λ`.

**Por que isso importa**: antes, o resultado da simulação podia mudar
(ligeiramente) dependendo de rodar em 1 tick de 1h ou em 60 ticks de 1 min —
um problema real de "determinismo" quando o jogador muda a velocidade do
tempo (x1/x5/x20) no painel de debug. Agora os três são **matematicamente
independentes do tamanho do passo**, confirmado pelos testes
`StepSvr_IsStepSizeIndependent`, `StepVolume_IsStepSizeIndependent` e
`StepLactate_IsStepSizeIndependent` (comparam 1 passo grande vs. dezenas de
passos pequenos cobrindo o mesmo tempo total, com tolerância de 0,0005).

`InfectionGrowth`/`InfectionKill` permanecem Euler explícito por razão de
arquitetura (ver seção de infecção acima) — aceitável na taxa/tamanho de passo
usados, mas registrado aqui como uma assimetria consciente, não um descuido.

## Testes automatizados (77 testes, EditMode, fora do Play Mode)

`ClinicalFormulasTests.cs` (30 testes) e `PatientSimulationTests.cs` (18
testes). Além dos testes de fórmula isolada já existentes, esta revisão
adicionou:

- Regressão do bug original: `Map_WithHighNorepinephrineDose_StaysBoundedByAfterloadFeedback`,
  `NorepinephrineEffect_InNonVasoplegicPatient_DoesNotProduceExtremeSvr`.
- Vasoplegia leve vs. grave: `NorepinephrineEffect_IsBlunted_BySevereVasoplegia`,
  `Norepinephrine_InSevereVasoplegia_RespondsLessThanInMildDisease`.
- Doses crescentes: `NorepinephrineEffect_MarginalGain_ShrinksAtHigherDoses`,
  `Norepinephrine_IncreasingDoses_ShowDiminishingMapGainsAndRisingHeartRate`.
- Volume grande: `LargeCrystalloidVolume_ShowsDiminishingReturnsNotUnboundedRise`,
  `VolumeFactor_DiminishingReturns_AboveNormovolemia`.
- Antibiótico precoce vs. tardio: `EarlyAntibiotic_OutperformsLateAntibiotic_AtSameFollowUpTime`.
- Hipóxia como causa de óbito: `RespiratoryFailure_IsAPossibleCauseOfDeath_WhenOxygenationNeverTreated`.
- Independência do tamanho do passo: `StepSvr_IsStepSizeIndependent`,
  `StepVolume_IsStepSizeIndependent`, `StepLactate_IsStepSizeIndependent`.
- Hipovolemia extrema: `Map_SevereHypovolemia_CanProduceCriticallyLowPressure`.
- SpO₂ crítica: `SpO2_SevereLungInjury_CanBeCriticallyLow_EvenWithMaxOxygen`,
  `SpO2_OxygenBenefit_IsBluntedBySeverity`.

Todos os 48 passavam (`run_tests` / Unity Test Runner, modo EditMode) até a
Revisão 3. A Revisão 4 (correção de P1-P5) atualizou as assinaturas afetadas
(`StepLactate`, `UrineOutput`, `Consciousness`→`ConsciousnessScoreTarget`/
`ConsciousnessLevelFromScore`) e acrescentou testes novos para cada achado
corrigido — ver a lista de testes em cada subseção "P1"-"P5" acima. Total
agora: **77 testes**, todos passando.

## Validação fisiológica — bateria de cenários (Revisão 3)

> **Nota (Revisão 4)**: as tabelas abaixo são o registro histórico "antes"
> da correção — é aqui que os achados P1-P5 foram originalmente
> identificados. Os itens marcados com ⚠ (PAD negativa), o salto instantâneo
> de consciência, o teto de FR e a baixa resposta a 0,05 mcg/kg/min **já
> foram corrigidos** — ver "Revisão 4 — Correção dos achados P1-P5" e
> "Revisão 4 — Comparação antes/depois" mais abaixo neste mesmo documento.

### Metodologia

15 cenários padronizados foram executados diretamente sobre
`PatientSimulationController` (fora do Unity Play Mode, mesma técnica dos
testes automatizados), registrando **todas** as variáveis pedidas — FC,
PAS/PAD/PAM, FR, SpO₂, temperatura, lactato, diurese, consciência, volume
circulante efetivo, RVS e intensidade da infecção — em checkpoints de tempo
(grade padrão: 0, 1, 2, 3, 4, 6, 8, 10, 12, 16, 20, 24, 30, 36, 48h; grade mais
fina de 15-30 min para os cenários de noradrenalina). As séries completas
estão em `Docs/ValidationData/<nome>.csv`; nenhum arquivo de código foi
alterado para gerar esta bateria. Peso do paciente: 70 kg em todos os casos.

**Nota sobre "sepse moderada" vs. "choque séptico grave"**: a arquitetura
atual (`SepsisDiseaseProcess`) não expõe um parâmetro de gravidade inicial —
existe uma única trajetória (infecção parte de 0,05 e cresce logisticamente a
0,20/h se não tratada). Por isso, "sepse moderada sem tratamento" e "choque
séptico grave sem tratamento" foram tratados como **dois recortes temporais
da mesma trajetória não tratada** (fase inicial compensada, ~0–6h, vs. fase
tardia de choque, ~10h em diante) em vez de dois cenários paramétricos
distintos. Isso é uma limitação da arquitetura atual (ver checklist final),
não uma escolha arbitrária desta validação.

### Tabela 1 — Sepse não tratada (moderada → choque séptico grave → óbito)

Fonte: `01_untreated_baseline.csv`.

| t(h) | FC | PA (PAS/PAD) | PAM | FR | SpO₂ | Temp | Consciência | Diurese | Lactato | Volume | RVS | Infecção |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 75 | 120/80 | 93,0 | 16,0 | 98% | 37,00 | Alerta | 70 | 1,00 | 1,000 | 1,000 | 0,050 |
| 4 (moderada) | 94,5 | 112/69 | 83,2 | 18,1 | 92,3% | 37,22 | Alerta | 92 | 0,69 | 0,956 | 0,917 | 0,104 |
| 6 (moderada) | 104,6 | 108/63 | 77,9 | 19,7 | 89,9% | 37,32 | Confuso | 92 | 0,89 | 0,921 | 0,883 | 0,148 |
| 10 (grave) | 133,9 | 96/47 | 63,0 | 27,2 | 82,7% | 37,61 | Sonolento | 64 | 1,89 | 0,812 | 0,780 | 0,278 |
| 12 (grave) | 152,4 | 88/36 | 53,6 | 33,1 | 78,0% | 37,81 | Não responsivo | 38,8 | 2,76 | 0,738 | 0,711 | 0,364 |
| 16 (grave) | 173,9 | 73/15 | 34,4 | 48,7 | 67,2% | 38,28 | Não responsivo | 3,1 | 5,50 | 0,559 | 0,554 | 0,560 |
| 20 (grave) | 159,8 | 62/**-1,7**⚠ | 19,5 | 50,0 | 57,3% | 38,75 | Não responsivo | 0 | 9,79 | 0,377 | 0,410 | 0,740 |
| 21,83 (óbito) | 154,9 | 59/**-6,9**⚠ | 14,9 | 50,0 | 53,8% | 38,82 | Não responsivo | 0 | 12,24 | 0,305 | 0,358 | 0,804 |

⚠ PAD negativa — ver **P1** na seção de comportamentos implausíveis abaixo.
Choque séptico (PAM<65 + lactato>2) é atingido por volta de t≈10-11h; óbito em
t≈21,8h. FR e FC deixam de crescer monotonicamente perto do óbito (FR satura
no teto de 50; FC cai de 173,9→154,9 — descompensação intencional, ver
Revisão 2).

### Tabela 2 — Fluido precoce vs. tardio vs. sem tratamento

Fonte: `02_fluid_early.csv` (bolus 2000 mL em t=1h), `03_fluid_late.csv`
(mesmo bolus em t=12h), `01_untreated_baseline.csv`.

| t(h) | PAM sem tto. | PAM fluido precoce | PAM fluido tardio | Lactato sem tto. | Lactato precoce | Lactato tardio |
|---|---|---|---|---|---|---|
| 2 | 87,1 | **92,4** | — | 0,69 | **0,58** | — |
| 12 | 53,6 | 62,3 | 53,6→(bolus)→53,6* | 2,76 | 1,70 | 2,76 |
| 16 | 34,4 | 39,6 | **41,5** | 5,50 | 4,19 | **4,44** |
| Óbito (h) | 21,83 | 22,67 | 22,92 | — | — | — |

*O bolus tardio é dado exatamente no instante do checkpoint t=12h, então o
efeito só aparece no checkpoint seguinte (t=16h).

**Achado**: um único bolus — precoce ou tardio — melhora PAM/lactato
temporariamente mas **não evita o óbito**, só o adia por ~1h. Isso é esperado
e clinicamente coerente (fluido isolado não trata a causa infecciosa), não um
defeito — mas reforça que os cenários de tratamento "combinado" abaixo
também precisam de redosagem para funcionar a longo prazo.

### Tabela 3 — Antibiótico precoce vs. tardio vs. sem tratamento

Fonte: `04_antibiotic_early.csv` (dose em t=0h), `05_antibiotic_late.csv`
(dose em t=10h).

| t(h) | Infecção sem tto. | Infecção abx precoce | Infecção abx tardio | PAM sem tto. | PAM abx precoce | PAM abx tardio |
|---|---|---|---|---|---|---|
| 6 | 0,148 | **0,007** | 0,148 (ainda sem droga) | 77,9 | **90,7** | 77,9 |
| 12 | 0,364 | 0,013 | 0,278→(dose em t=10)→0,166 | 53,6 | 89,8 | 65,1 |
| 16 | 0,560 | 0,028 | **0,037** | 34,4 | 88,0 | **69,1** (Alerta!) |
| 24 | óbito iminente | 0,123 (relapso) | 0,087 (relapso) | — | 76,9 | 63,7 |
| 30 | (morto) | 0,317 | 0,240 | — | 55,9 | 50,6 |
| Óbito (h) | 21,83 | **40,67** | **41,33** | — | — | — |

**Achado**: mesmo tardio (t=10h, já em choque estabelecido), o antibiótico
**dobra o tempo de sobrevida** (21,8h → 41,3h) e traz o paciente de volta a
"Alerta" por várias horas (t=16-20h) antes de recair quando a dose de 6h de
cobertura se esgota. O padrão "melhora → recaída sem redose" é o
comportamento pretendido (força redosagem periódica) e apareceu de forma
consistente e plausível nos dois casos.

### Tabela 4 — Dose-resposta da noradrenalina (dose fixa a partir de t=6h)

Fonte: `08_norepi_dose_*.csv`. Todos os 5 pacientes são idênticos até t=6h
(PAM=77,9, RVS=0,883, Infecção=0,148); a dose é dada uma única vez em t=6h e
mantida constante (é uma configuração de infusão contínua, não um bolus).

| Dose (unid. interna) | PAM em t=6,25h | ΔPAM (dose→dose) | RVS em t=6,25h | PAM em t=12h | Consciência em t=12h |
|---|---|---|---|---|---|
| 0 (referência) | 77,9 | — | 0,883 | 53,6 | Não responsivo |
| 0,05 | 79,3 | +1,4 | 0,905 | 55,4 | Não responsivo |
| 0,1 | 82,8 | +3,5 | 0,953 | 58,4 | Não responsivo |
| 0,2 | 88,1 | +5,3 | 1,053 | 64,7 | Não responsivo |
| 0,5 | 93,3 | +5,2 | 1,231 | 73,7 | Não responsivo |
| 1,0 | 95,6 | +2,3 | 1,322 | 76,0 | Não responsivo |

**Achados**: (a) resposta claramente não linear com ganho marginal
decrescente confirmado numericamente (a coluna ΔPAM cresce e depois encolhe:
+1,4 / +3,5 / +5,3 / +5,2 / +2,3 para incrementos de dose desiguais — ver
nota abaixo); (b) **mesmo na dose máxima (1,0), o paciente permanece "Não
responsivo" em t=12h** apesar de PAM=76 (aceitável) — porque a noradrenalina
não faz nada pela SpO₂ (78% nesse ponto), e a fórmula de consciência exige
PAM **e** SpO₂ acima do limiar para "Alerta". Isso é um resultado não-óbvio e
clinicamente defensável (pressor não trata hipóxia), destacado aqui como
validação positiva, não um problema. (c) A dose de 0,05 — um valor real de
"dose baixa" de noradrenalina em mcg/kg/min — produz um efeito quase
imperceptível no modelo; ver **P2** abaixo.

### Tabela 5 — Noradrenalina com volume adequado vs. sem volume

Fonte: `09_norepi_with_volume.csv` (bolus de 3000 mL em t=6h, dose 0,3 em
t=6,5h) vs. `10_norepi_without_volume.csv` (mesma dose 0,3 em t=6,5h, sem
nenhum fluido).

| t(h) | PAM com volume | PAM sem volume | Lactato com volume | Lactato sem volume | RVS (igual nos dois) |
|---|---|---|---|---|---|
| 7,0 | 102,3 | 89,2 | 0,70 | 0,87 | 1,134 |
| 12,0 | 87,9 | 69,6 | 0,50 | 1,25 | 0,957 |

**Achado adicional (teste dirigido)**: subindo a dose do paciente **sem**
volume para 0,85 (em vez de 0,3) até a PAM ficar próxima da do paciente
**com** volume+dose 0,3 (PAM 91,2 vs. 100,7 em t=8h), o lactato resultante
fica quase igual (0,59 vs. 0,50 — ambos "normais"). Isto é um comportamento
potencialmente implausível — ver **P3** abaixo: o modelo não consegue
distinguir uma PAM normalizada por volume de uma PAM normalizada por
vasoconstrição pura.

### Tabela 6 — Oxigênio isolado vs. sem tratamento

Fonte: `06_oxygen_isolated.csv` (O₂ nível 2 desde t=0) vs. `01_untreated_baseline.csv`.

| t(h) | SpO₂ sem O₂ | SpO₂ com O₂ isolado | PAM (idêntica nos dois) | Óbito (h, idêntico nos dois) |
|---|---|---|---|---|
| 1–6 | 94,7→89,9% | **100%** (platô) | idêntica | 21,83 |
| 10 | 82,7% | 94,8% | 63,0 | — |
| 16 | 67,2% | 77,3% | 34,4 | — |

**Achado**: O₂ isolado melhora SpO₂ substancialmente mas **não muda em nada**
FC/PA/PAM/lactato/infecção, e o paciente morre exatamente na mesma hora
(21,83h) pela via de choque refratário (PAM), não pela via respiratória. Isso
é o comportamento pretendido pela arquitetura (O₂ só alimenta a fórmula de
SpO₂) e é clinicamente defensável — O₂ suplementar isolado não trata choque
distributivo — mas reforça que, isoladamente, ele tem valor limitado no jogo
tal como implementado hoje.

### Tabela 7 — Tratamento combinado (abx + 2000 mL + noradrenalina 0,3 + O₂ nível 2, tudo em t=1h, dose única)

Fonte: `07_combined_treatment.csv`.

| t(h) | PAM | Infecção | Consciência | Observação |
|---|---|---|---|---|
| 2 | 105,7 | 0,054 | Alerta | Estabilização rápida |
| 8–24 | 100,7→106,4→100,7 | 0,008→0,121 | Alerta | Platô estável enquanto abx cobre (~6-8h) |
| 30 | 83,2 | 0,313 | Alerta (ainda) | Antibiótico já esgotado, infecção voltando a crescer |
| 36 | 45,5 | 0,602 | Não responsivo | Recaída total |
| 44,67 | 14,9 | 0,896 | Não responsivo | **Óbito** |

**Achado**: mesmo a combinação completa das 4 intervenções, aplicada
precocemente, **falha e o paciente morre em ~45h** — porque, neste protocolo
de teste, cada intervenção foi dada **uma única vez** (antibiótico não
redosado após a janela de ~6-8h de cobertura; noradrenalina e fluido
mantidos no valor inicial). Isto não é um defeito do modelo — é o
comportamento pretendido (força reavaliação e redosagem periódica pelo
jogador) — mas é um resultado importante de documentar: "tratamento
combinado" sozinho, sem repetição ao longo do plantão, não é suficiente
neste modelo, assim como não seria na vida real.

### Comportamentos potencialmente implausíveis identificados

Nenhum coeficiente foi alterado para corrigir os itens abaixo — cada um lista
o parâmetro/mecanismo exato responsável, para decisão e correção em uma
calibração futura dedicada.

**P1 — Pressão diastólica fica negativa em choque terminal.**
Observado em **todos os 7 cenários** desta bateria que evoluem a óbito
(PAD entre -1,7 e -7,7 mmHg nos 1-2 checkpoints antes do óbito).
*Parâmetro responsável*: `ClinicalFormulas.PulsePressure(svr) =
clamp(40*(2-svr), 15, 90)` depende **somente** da RVS, nunca da PAM. Em RVS
muito baixa (~0,3-0,4, vasoplegia terminal), PP sobe a ~65-70 mmHg. Como
`PAD = PAM - PP/3`, quando a PAM já caiu abaixo de ~20 mmHg, `PP/3` (~22-23)
passa a exceder a própria PAM, forçando PAD negativa. A PAM (a variável que
efetivamente governa o resto do modelo) permanece correta; o problema é
isolado ao par PAS/PAD exibido. Correção provável (não aplicada):
acoplar `PulsePressure` a uma fração da própria PAM, não deixá-la
independente.

**P2 — A curva dose-resposta da noradrenalina implica que doses clínicas
baixas (0,05 mcg/kg/min) são quase inertes.**
Observado na Tabela 4: dose 0,05 → ΔPAM=+1,4 mmHg (~5% do efeito máximo).
*Parâmetro responsável*: `NorepinephrineED50=0,3` combinado com
`NorepinephrineHillCoefficient=1,6`, **assumindo que a "dose" interna do
simulador corresponde 1:1 a mcg/kg/min reais** — não há, em nenhum lugar do
código ou da UI, um mapeamento validado entre a unidade interna (0-1,5) e
mcg/kg/min. Esta bateria assumiu correspondência 1:1 por não haver
alternativa documentada. Duas resoluções possíveis, nenhuma aplicada: (a)
recalibrar `ED50` para responder de forma clinicamente realista a mcg/kg/min
reais (ex.: ED50 bem menor, ~0,1), ou (b) deixar explícito que a "dose" do
painel de debug é uma unidade de jogo arbitrária e nunca rotulá-la como
mcg/kg/min na UI final.

**P3 — Lactato/perfusão não distingue PAM alcançada por volume de PAM
"mascarada" por vasoconstrição.**
Confirmado experimentalmente (Tabela 5): PAM≈90-100 obtida via volume+dose
baixa produz lactato≈0,50-0,70; PAM≈91 obtida via dose alta **sem** volume
produz lactato≈0,59 — praticamente indistinguível, ambos "normais".
*Parâmetro/mecanismo responsável*: `PerfusionIndex(map) =
clamp(map/93, 0, 1.3)` — a perfusão tecidual (e portanto todo o cálculo de
lactato) é função **exclusiva** da PAM; não existe nenhuma variável de
fluxo/DO₂ separada da pressão. Isso contradiz o ensinamento clínico real de
que vasoconstrição sem volume adequado pode piorar a perfusão
microcirculatória mesmo com PAM normalizada. Não corrigido — exigiria uma
variável de "fluxo efetivo" distinta da PAM, o que é uma mudança de modelo
maior que uma calibração de coeficiente.

**P4 — O teto de frequência respiratória (50 irpm) deixou de ser um caso
extremo raro.**
Observado: em **todos** os cenários que evoluem a choque grave/óbito nesta
bateria, a FR bate exatamente no teto de 50 a partir de ~t=16-20h e
permanece lá até o óbito. *Parâmetro responsável*: `RespiratoryRate` soma
termos lineares (hipóxia + lactato + febre) sem nenhum mecanismo de
saturação ou fadiga muscular ventilatória — o clamp de 50 está fazendo o
trabalho que uma dinâmica deveria fazer, exatamente a mesma classe de
problema que a PAM tinha antes da Revisão 2. Não corrigido nesta rodada
(escopo era validação, não recalibração), mas fica registrado que o mesmo
princípio ("teto emergente, não artificial") aplicado à PAM deveria, em
algum momento, ser aplicado à FR também.

**P5 — Recuperação de consciência sem inércia (confirma limitação já
conhecida, agora com exemplo concreto).**
No cenário de antibiótico tardio (Tabela 3), o paciente passa de "Não
responsivo" (t=10h) para "Alerta" (t=16h) e volta a "Confuso" (t=24h) de
forma instantânea a cada tick, sem nenhum atraso. Já listado como limitação
conhecida na Revisão 2 (item 7); esta bateria apenas confirma que o efeito
aparece na prática, não só em teoria.

### O que a bateria confirmou como plausível (não são achados de problema)

- Sepse não tratada progride de forma monotonicamente coerente de
  compensada → choque → óbito, com FC/FR subindo, PAM/consciência caindo,
  lactato subindo com atraso realista em relação à queda de PAM.
- Antibiótico precoce vs. tardio: diferença clara e clinicamente sensata
  (dobra o tempo de sobrevida mesmo dado tardiamente).
- Fluido isolado: melhora temporária, não evita óbito sem controle da fonte
  — correto.
- Oxigênio isolado: melhora SpO₂, não muda desfecho de choque distributivo —
  correto.
- Dose-resposta da noradrenalina: monotônica, com ganho marginal decrescente
  e resposta bloqueada por vasoplegia grave, exatamente como desenhado na
  Revisão 2.
- Tratamento combinado sem redosagem falha a longo prazo — reforça a
  necessidade de reavaliação periódica, um resultado educacional desejável.

## Revisão 4 — Comparação antes/depois (bateria de 15 cenários)

Os mesmos 15 cenários da Revisão 3 foram reexecutados byte-a-byte com os
mesmos parâmetros de entrada (doses, tempos, volumes). Séries "antes" em
`Docs/ValidationData/*.csv`, "depois" em `Docs/ValidationData/Revision4/*.csv`
(mesmos nomes de arquivo). Resumo dos pontos mais relevantes:

| Cenário | Variável | Antes (Revisão 3) | Depois (Revisão 4) |
|---|---|---|---|
| Sepse não tratada, óbito (t=21,83h) | PAD | **-6,9 mmHg** ⚠ | **+11,4 mmHg** ✓ |
| Sepse não tratada, t=20h | PAD | -1,7 mmHg ⚠ | +15,1 mmHg ✓ |
| Sepse não tratada, t=12h | Consciência | Não responsivo (instantâneo) | Sonolento (score 0,325 — transição em curso) |
| Sepse não tratada, t=16h | FR | 50,0 (teto exato) | 49,2 (assíntota, nunca 50) |
| Antibiótico tardio, t=16h | Consciência | Alerta (salto direto de Sonolento) | Alerta (score 0,768, passou por Confuso em t=12h) |
| Noradrenalina 0,05 mcg/kg/min, Δ 15min | ΔPAM | +1,4 mmHg | **+9,0 mmHg** |
| Noradrenalina 1,0 mcg/kg/min, Δ 15min | ΔPAM | +17,7 mmHg | +18,8 mmHg (Emax preservado) |
| Com volume (3L+dose 0,3) vs. sem volume (dose 0,85, PAM~igualada), t=8h | Lactato | 0,50 vs. 0,59 (quase igual) | **0,64 vs. 1,12** (quase o dobro) |
| idem | Perfusão explícita | (não existia) | **0,890 vs. 0,737** |
| idem | Diurese | 92,0 vs. 92,0 (idêntica) | 81,9 vs. 67,8 (diferente) |

**O que NÃO mudou** (confirmando que a correção foi cirúrgica): tempo de
óbito na sepse não tratada permanece 21,83h (critérios de óbito checam só
PAM/SpO2, não tocados); infecção/RVS/volume seguem exatamente as mesmas
trajetórias numéricas em todos os cenários (Revisão 4 não mexeu em
`InfectionGrowth`, `StepSvr`, `StepVolume`, `SvrTarget`); antibiótico
precoce vs. tardio continua mostrando a mesma diferença de PAM/infecção de
antes, só que agora com uma leitura de consciência mais gradual.

**Efeito colateral notado (não é regressão)**: como a diurese agora também é
sensível ao fluxo (não só à PAM), alguns valores de diurese intermediários
mudaram ligeiramente mesmo sem intervenção nova (ex.: sepse não tratada t=8h:
89,4→81,7 mL/h) porque `FluxoEfetivo` já não é exatamente 1,0 nesse ponto
(RVS e volume já se afastaram do basal). Isso é esperado e correto — a
diurese sempre deveria ter refletido o fluxo, não só a pressão.

## Log de calibração (Revisão 2) — resumo das mudanças

| Constante/função | Antes | Agora | Motivo |
|---|---|---|---|
| `NorepinephrineEmax` | 0,9 | 0,6 | Efeito farmacológico máximo mais plausível |
| Curva de dose da noradrenalina | Emax simples | Hill (n=1,6) | Resposta sigmoide, ganho marginal decrescente |
| Responsividade à noradrenalina | inexistente | `1 - 0.5*I` (piso 0,5) | Resistência a catecolaminas em choque grave |
| Efeito cronotrópico da noradrenalina | inexistente | `+5 bpm/dose` | Sinal de taquicardia/risco em dose alta |
| Limite da PAM | `clamp(MAP, 5, 150)` | `AfterloadFactor(RVS)` + rail largo `[5,180]` | Teto emergente da hemodinâmica, não artificial |
| `VolumeFactor` | `clamp(0.5+0.5V, 0.5, 1.15)` | curva suave, piso→0,15, teto assintótico | Sem cotovelos; hipovolemia extrema agora é perigosa de verdade |
| `StepSvr`/`StepTemperature` | interpolação linear capada | relaxamento exponencial exato | Independência do tamanho do passo |
| `StepVolume` | Euler explícito | decaimento exponencial exato | idem |
| `StepLactate` | Euler explícito | solução linear exata | idem |
| Curva do antibiótico | rampa linear + platô + decaimento linear | onset/decaimento exponenciais suaves | Sem "cotovelos"; efeito ainda não-imediato |
| `LungInjuryCoefficient` (SpO₂) | 18 | 55 | Sepse grave sem O₂ deve poder ficar realmente hipóxica |
| Benefício de O₂ | fixo (6/12) | `6/14`, atenuado por shunt (`×(1-0.5·I)`) | O₂ simples não corrige shunt grave |
| Piso de SpO₂ | 70 (clamp rígido) | 35 (rail numérico) | Viabiliza hipóxia crítica/óbito respiratório |
| Critério de óbito | só PAM<15 sustentada | PAM<15 **ou** SpO₂<45 sustentada | Insuficiência respiratória isolada agora pode matar |

## Pontos que ainda precisam de validação médica

Esta lista substitui a lista de "simplificações conhecidas" da revisão
anterior — itens resolvidos nesta calibração foram removidos; os que
permanecem (ou foram introduzidos por ela) estão aqui:

1. **Nenhuma constante numérica é baseada em literatura quantitativa** — nem
   antes, nem agora. `Emax=0,6`, `ED50=0,1 mcg/kg/min`, `Hill=1,4`,
   `Responsividade mínima=0,5`, `AfterloadSensitivity=0,6`,
   `LungInjuryCoefficient=55`, `ConsciousnessTimeConstantHours=0,5h`,
   limiares de consciência `0,75/0,50/0,25`, `RespiratoryDriveScale=17`,
   `AutoregulationThresholdMAP=65` etc. foram calibrados para produzir
   comportamento **direcionalmente** correto e uma progressão jogável — não
   foram ajustados contra dados clínicos reais de dose-resposta. **Precisa de
   revisão por alguém com formação em farmacologia/medicina intensiva.**
2. **Sem farmacocinética real** (meia-vida, metabolismo) para nenhuma droga —
   ainda curvas de onset/platô/decaimento arbitrárias, agora suaves em vez de
   lineares, mas não baseadas em modelos PK reais.
3. **Sem interação entre drogas** (ex.: antibiótico nefrotóxico + hipovolemia,
   ou noradrenalina + arritmia pré-existente).
4. **AfterloadFactor e a resistência a catecolaminas são inéditos nesta
   revisão e inteiramente inventados** para resolver o problema de
   superrresposta — a FORMA (débito cai com pós-carga alta, catecolamina
   perde efeito em choque grave) é fisiologicamente bem estabelecida, mas os
   COEFICIENTES (0,6 e 0,5) não vêm de nenhuma referência quantitativa.
   **Item prioritário para revisão médica.**
5. **Sem contratilidade/débito cardíaco explícitos** — `AfterloadFactor` é uma
   correção de primeira ordem sobre `PAM = RVS × fator`, não um modelo de
   Frank-Starling completo nem contempla disfunção miocárdica séptica
   (cardiomiopatia séptica), que na vida real reduziria ainda mais o débito.
6. **Critério de óbito ainda é um par de limiares duros** (PAM ou SpO₂
   sustentados), sem PCR modelada, sem via de reanimação (RCP), sem
   possibilidade de morte por falência renal/hepática isolada.
7. ~~Consciência recalculada a cada tick sem histerese~~ **[RESOLVIDO na
   Revisão 4 — ver P5]**: agora tem um score contínuo com inércia (τ=30min).
   O limiar de "choque séptico" (evento `ThresholdCrossedEvent`), porém,
   **continua** sem histerese própria — só a consciência foi corrigida.
8. **Um único patógeno/uma única doença** — arquitetura permite múltiplos
   `DiseaseProcess`, não testado com mais de um rodando ao mesmo tempo.
9. **A curva Hill/responsividade da noradrenalina não modela taquifilaxia**
   (perda de efeito com uso prolongado contínuo) nem desmame — só depende da
   dose instantânea e da vasoplegia atual.
10. **RR, diurese, pulso e consciência não foram tocados nesta calibração** —
    revisados e considerados aceitáveis para o escopo atual, mas também não
    validados contra literatura.

### Itens da validação (Revisão 3) — status após a Revisão 4

11. ~~PAD pode ficar negativa em choque terminal~~ **[RESOLVIDO — ver P1]**.
12. ~~Sem mapeamento validado entre a unidade interna de dose da noradrenalina
    e mcg/kg/min reais~~ **[RESOLVIDO — ver P2]**. O valor exato de
    `ED50=0,1 mcg/kg/min` continua arbitrário (item 1) — o que foi resolvido
    é a **existência** de um mapeamento documentado, não a certeza de que
    0,1 é o número clinicamente correto.
13. ~~Lactato/perfusão dependem só de PAM, sem variável de fluxo/DO₂
    separada~~ **[RESOLVIDO — ver P3]**: `EffectivePerfusion` agora existe e
    governa lactato/diurese/consciência. Os COEFICIENTES da nova variável
    (`AfterloadSensitivity=0,6`, `AutoregulationThresholdMAP=65`) são tão
    arbitrários quanto os que já existiam (item 1/4) — o que mudou é a
    ESTRUTURA do modelo (fluxo separado de pressão), não a certeza numérica.
14. ~~Teto de FR (50) rotineiramente atingido em cenários graves~~
    **[RESOLVIDO — ver P4]**: agora uma curva saturável; `RespiratoryDriveScale=17`
    é arbitrário (item 1).
15. **Não existe parâmetro de gravidade inicial da sepse** — ainda não
    resolvido (fora do escopo desta correção, que tratou só de P1-P5).
    "Moderada" e "grave" continuam sendo dois recortes de tempo da mesma
    curva logística.
16. **Bateria de validação não testou combinações de redosagem periódica** —
    ainda não resolvido; continua valendo após a Revisão 4.

### Itens novos introduzidos pela Revisão 4

17. **`EffectivePerfusion` não tem inércia própria** — é recalculada
    algebricamente a cada tick a partir de PAM/Volume/RVS atuais (só a
    consciência, que a consome, tem inércia). Um fluxo/DO₂ real também teria
    alguma dinâmica própria (ex.: resposta autonômica gradual), não
    modelada aqui.
18. **`min(perfusão, oxigenação)` para o alvo de consciência é uma escolha de
    modelagem, não uma fórmula clínica validada** — capta a ideia de que o
    pior dos dois fatores domina, mas a relação real entre PAM, fluxo, SpO₂
    e nível de consciência é mais complexa (edema cerebral, autorregulação
    cerebral específica, etc., nada disso modelado).
19. **O evento de "choque séptico" (`ThresholdCrossedEvent`) ainda usa PAM e
    lactato crus, sem histerese e sem o novo `EffectivePerfusion`** — não foi
    tocado nesta revisão (escopo era só P1-P5); pode valer revisão conjunta
    no futuro para consistência com a consciência.
20. **A curva de `PulsePressureFraction` (0,15-0,85) e o valor basal
    (0,43=40/93) não vêm de dados hemodinâmicos reais de pressão de pulso
    por RVS** — a FORMA (pressão de pulso alarga com vasodilatação, estreita
    com vasoconstrição) é bem estabelecida; a magnitude exata não.

## Interface de debug (`ClinicalDebugUI`)

Painel IMGUI sempre visível mostrando sinais vitais e variáveis internas em
tempo real. **Revisão 4**: rótulo da noradrenalina agora mostra "mcg/kg/min"
explicitamente, os botões de titulação passaram de ±0,1 para ±0,05 (para
combinar com a granularidade real de titulação e com as doses testadas na
validação), e duas linhas novas nas "variáveis internas" —
`EffectivePerfusion` ("Perfusão efetiva (fluxo)") e `ConsciousnessScore`
("Score de consciência"). Fora isso, mesmos botões de intervenção
(cristaloide 500/1000 mL, O₂ ar-ambiente/cateter/máscara, antibiótico) e
controles de tempo (pausar, x1/x5/x20) de antes. Nenhuma outra mudança de
código; a UI continua sem depender de `ClinicalFormulas`.

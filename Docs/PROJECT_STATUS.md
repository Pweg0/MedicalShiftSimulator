# MedicalShiftSimulator — Estado do Projeto (contexto para a próxima sessão)

> Documento de handoff. Objetivo: uma nova sessão (ou pessoa) consegue entender
> o que existe, onde, por quê, e o que falta, sem precisar reler a conversa
> inteira. Escrito em 2026-09-05, ao final da "Revisão 4" do modelo clínico.
> Detalhes profundos de fórmulas/calibração do modelo clínico estão em
> `Docs/ClinicalModel.md` (este arquivo referencia, não duplica).

## O que é o projeto

Protótipo Unity (URP, Input System novo) de um simulador de plantão em UPA
(Unidade de Pronto Atendimento): o jogador controla um médico, anda pela
planta em câmera isométrica, e há um núcleo de simulação clínica (paciente
com sepse/choque séptico) rodando por trás, desacoplado da apresentação
Unity. Ainda **não há**: interface médica final, inventário, enfermagem,
exames completos, múltiplas doenças, ou pacientes múltiplos — tudo isso é
trabalho futuro, fora do escopo até agora.

## Cena e cenário

- Cena única: `Assets/Scenes/SampleScene.unity`.
- Câmera: `Main Camera`, ortográfica, isométrica, inclinação (`pitchAngle`)
  configurável no Inspector (padrão 50°, faixa 40-70°), com órbita (botão
  direito do mouse), zoom (roda do mouse) e ocultação automática de paredes
  entre câmera e personagem.
- Planta da UPA: hierarquia `UPA` na raiz da cena, contendo o quarto
  hospitalar original (`Hospital Room`, preservado desde o início — funciona
  como "Consultório 1") mais 12 setores construídos depois: Corredor
  Principal, Entrada de Ambulância, Sala Vermelha (2 leitos), Consultório 2,
  Posto de Enfermagem, Sala de Espera, Observação (4 leitos), Sala de
  Medicação, Coleta e Laboratório, Radiologia, Farmácia e Estoque, Triagem,
  Recepção. Tudo graybox (paredes/pisos simples, sem decoração).
- Personagem do jogador: `Doctor (Provisional)` (cápsula azul), com
  `CharacterController` + `PlayerController` (WASD, eixo mundo, não gira com
  a câmera) + `PlayerInteractor` (tecla E, detecta `IInteractable` por
  `OverlapSphere`).
- Paciente de exemplo: `Paciente (Sepse - Provisorio)` (cápsula vermelha) num
  leito da Sala Vermelha, com `PatientSimulationBehaviour` +
  `ClinicalDebugUI` (painel IMGUI sempre visível, mostra sinais vitais e
  botões das 4 intervenções).

## Scripts — onde está cada coisa

### `Assets/Scripts/Player/` e `Assets/Scripts/Interaction/` (Assembly-CSharp, sem asmdef próprio)
- `PlayerController.cs` — movimento WASD (Input System, `Keyboard.current`),
  eixo do mundo, `CharacterController`.
- `IInteractable.cs`, `InteractableObject.cs`, `PlayerInteractor.cs` —
  sistema de interação genérico (tecla E, `OverlapSphere`). Poucos objetos
  usam isso hoje (ex.: Patient Monitor, Bedside Cabinet no quarto original).

### `Assets/Scripts/Camera/` (Assembly-CSharp)
- `IsoCameraFollow.cs` — segue o personagem, órbita (yaw) suavizada por
  `SmoothDampAngle`, zoom via `Camera.orthographicSize`, inclinação fixa
  configurável (`pitchAngle`).
- `WallFade.cs` — componente por parede: cria uma instância própria de
  material (nunca modifica o material compartilhado), anima alpha (~0,2s) e
  desabilita o `Renderer` quando totalmente oculta (rede de segurança caso a
  transparência do shader URP não renderize perfeitamente). Collider nunca é
  tocado.
- `CameraWallOcclusion.cs` — no `Main Camera`, faz um leque de 5
  `SphereCastNonAlloc` (centro + laterais/cima/baixo) até o personagem,
  filtra por layer `Walls` + componente `WallFade`, chama `SetHidden`.
  Suporta múltiplas paredes simultâneas, restaura automaticamente.
- Layer `Walls` (slot 8) foi criada no projeto e atribuída a todos os ~65
  objetos de parede da cena (script batch, não um componente persistente).

### `Assets/Scripts/Clinical/` (assembly **`MedicalShiftSimulator.Clinical`**, `Clinical.asmdef` com `noEngineReferences: true` — **zero UnityEngine**, garantido em tempo de compilação)
- `Core/VitalSigns.cs` — struct de sinais vitais + `ConsciousnessLevel` enum.
- `Core/ClinicalClock.cs` — relógio simulado (minutos), independente de
  `Time` do Unity.
- `Core/ClinicalFormulas.cs` — **todas** as fórmulas puras e estáticas
  (PAM, RVS, volume, noradrenalina, SpO₂, FR, temperatura, lactato,
  perfusão, consciência, infecção, antibiótico). Ver `Docs/ClinicalModel.md`
  para cada fórmula com racional e constantes.
- `Core/PatientState.cs` — estado completo de um paciente (drivers internos +
  `Vitals` derivados). `RecalculateVitals(deltaHours)` é o método central.
  Critério de óbito: PAM<15 por ≥2min **ou** SpO₂<45 por ≥5min.
- `Events/ClinicalEvents.cs`, `Events/ClinicalEventBus.cs` — pub/sub simples
  (`VitalsUpdatedEvent`, `InterventionAppliedEvent`, `ThresholdCrossedEvent`,
  `PatientDiedEvent`).
- `Diseases/DiseaseProcess.cs` (abstrato), `Diseases/SepsisDiseaseProcess.cs`
  — única doença implementada: sepse, crescimento logístico + leak capilar.
- `Treatments/TreatmentEffect.cs` (abstrato) + 4 implementações:
  `CrystalloidBolusEffect`, `OxygenTherapyEffect`,
  `NorepinephrineInfusionEffect`, `AntibioticEffect`. Únicas 4 intervenções
  que existem hoje.
- `PatientSimulationController.cs` — orquestra clock + doença + tratamentos +
  eventos; `Tick(deltaMinutes)` é chamado pelo host (Unity ou um teste).

### `Assets/Scripts/ClinicalBridge/` (assembly `MedicalShiftSimulator.ClinicalBridge`, referencia `Clinical`, **não** referencia `ClinicalFormulas` diretamente — só a API pública do controller)
- `PatientSimulationBehaviour.cs` — MonoBehaviour fino, converte
  `Time.deltaTime` em minutos simulados (`simulatedMinutesPerSecond`,
  ajustável). Zero lógica clínica.
- `ClinicalDebugUI.cs` — painel IMGUI (não é a UI médica final). Mostra
  FC/PA/PAM/FR/SpO₂/Temp/Consciência/Diurese/Lactato + variáveis internas
  (infecção, volume, RVS, **perfusão efetiva**, **score de consciência**) +
  botões: Cristaloide 500/1000mL, O₂ (3 níveis), Noradrenalina ±0,05
  **mcg/kg/min** (rotulado explicitamente), Antibiótico, controles de tempo
  (pausar, x1/x5/x20).

### `Assets/Tests/EditMode/` (assembly `MedicalShiftSimulator.Clinical.Tests`)
- `ClinicalFormulasTests.cs` + `PatientSimulationTests.cs` — **77 testes**
  NUnit, todos passando, rodam fora do Play Mode (`run_tests` do MCP ou
  Unity Test Runner). Cobrem cada fórmula isoladamente e cenários de ponta a
  ponta (deterioração, choque, óbito, cada intervenção isolada e combinada,
  regressões de bugs já corrigidos).

## Histórico de fases (ordem cronológica das sessões até agora)

1. **Inspeção inicial** da cena (só leitura).
2. **Fase 0**: planta da UPA construída do zero (preservando o quarto
   original), câmera isométrica fixa, `PlayerController`/`PlayerInteractor`
   criados, testado em Play Mode.
3. **Fase 1**: núcleo de simulação clínica criado (sepse/choque séptico como
   vertical slice), arquitetura `Clinical`/`ClinicalBridge` estabelecida,
   paciente de exemplo na Sala Vermelha, painel de debug, 27 testes iniciais.
4. **Revisão 2 (calibração técnica)**: corrigiu a resposta exagerada da
   noradrenalina (PAM batendo no teto de 150), trocou integrações Euler por
   soluções fechadas exatas (independência do tamanho do passo), removeu
   pisos/tetos artificiais de SpO₂/volume, adicionou 2ª via de óbito
   (hipóxia). 48 testes.
5. **Melhorias de câmera**: órbita horizontal (botão direito), zoom (roda do
   mouse), ocultação automática de paredes (`WallFade`+`CameraWallOcclusion`),
   depois ajuste de inclinação para 50° a pedido do usuário.
6. **Revisão 3 (validação fisiológica)**: bateria de 15 cenários padronizados
   executada e documentada, sem alterar código. Achou 5 comportamentos
   implausíveis (P1-P5, ver abaixo) com causa-raiz identificada.
7. **Revisão 4 (correção de P1-P5)**: corrigiu exatamente os 5 achados,
   nada mais. 69→77 testes. Bateria de 15 cenários reexecutada e comparada
   antes/depois.
8. **Este documento** — handoff para a próxima sessão.

## Estado do modelo clínico (resumo — detalhes completos em `Docs/ClinicalModel.md`)

- Doença: sepse (única). Cresce logisticamente (`0,20/h`) se não tratada,
  causa vasodilatação (RVS cai) e leak capilar (volume cai) proporcionais à
  carga infecciosa.
- Hemodinâmica: `PAM = PAM_basal × RVS × VolumeFactor(Volume) ×
  AfterloadFactor(RVS)` — o teto de PAM é **emergente** (débito cai com
  pós-carga alta), não um clamp. Pressão de pulso agora é uma fração da
  própria PAM (nunca gera PAD negativa).
- **Fluxo/perfusão explícita** (`PatientState.EffectivePerfusion`, novo na
  Revisão 4): `VolumeFactor × AfterloadFactor × PortãoDeAutorregulação(PAM)`.
  Governa lactato, diurese e o alvo de consciência — **não** é a mesma coisa
  que PAM (duas formas de atingir a mesma PAM podem ter perfusões
  diferentes).
- Consciência: score contínuo 0-1 com inércia (τ=30min), níveis discretos
  (Alerta/Confuso/Sonolento/Não responsivo) são só uma leitura do score.
- Noradrenalina: dose em **mcg/kg/min reais** (ED50=0,1, Hill=1,4, Emax=0,6),
  efeito bloqueado por vasoplegia grave (resistência a catecolaminas),
  efeito cronotrópico direto (+5bpm/dose) sinaliza dose excessiva.
- FR: resposta saturável (assíntota ~50, nunca clamp duro).
- Óbito: PAM<15 sustentada OU SpO₂<45 sustentada (duas causas independentes).
- **Nenhuma constante é validada contra literatura médica real** — tudo
  calibrado para comportamento direcionalmente plausível e jogável. Ver a
  checklist "Pontos que ainda precisam de validação médica" em
  `Docs/ClinicalModel.md` (20 itens catalogados) antes de qualquer uso além
  de prototipagem.

## Dados de validação

- `Docs/ValidationData/*.csv` — bateria de 15 cenários, versão "antes" da
  Revisão 4 (é aqui que os achados P1-P5 foram documentados).
- `Docs/ValidationData/Revision4/*.csv` — mesma bateria, "depois" das
  correções, mesmos nomes de arquivo para comparação direta.
- Cenários cobertos: sepse não tratada (moderada/grave), fluido precoce/
  tardio, antibiótico precoce/tardio, oxigênio isolado, tratamento
  combinado, noradrenalina em 5 doses (0,05/0,1/0,2/0,5/1,0 mcg/kg/min),
  noradrenalina com/sem volume adequado.

## Como trabalhar neste projeto (coisas aprendidas na prática)

- **MCP Unity**: use `mcp__unity__execute_code` para rodar C# arbitrário no
  Editor (funciona em Edit Mode e Play Mode) — é a forma mais confiável de
  testar `PatientSimulationController` diretamente (sem precisar de Play
  Mode) e de fazer operações em lote na cena (ex.: criar/configurar objetos).
  O compilador interno (CodeDom) é limitado a ~C#6: **sem** local functions,
  **sem** tuplas como variável local dentro do snippet (mas chamar um método
  do projeto que RETORNA uma tupla funciona normalmente). Use
  `System.Func<>`/`System.Action<>` como "funções locais".
- **Reflection entre assemblies**: como `execute_code` não referencia
  `MedicalShiftSimulator.Clinical`/`ClinicalBridge` diretamente, acesse tipos
  via `System.Type.GetType("Namespace.Tipo, NomeDaAssembly")` e invoque via
  reflection.
- **Domain reload / Play Mode**: teleportar um objeto com `CharacterController`
  via `transform.position = ...` sem desabilitar o componente primeiro causa
  comportamento errático (o controller usa uma posição em cache). Sempre
  `cc.enabled=false; transform.position=X; cc.enabled=true;`.
- **`run_tests` (EditMode)** falha com erro claro se o Editor estiver em Play
  Mode — sempre `manage_editor action=stop` antes.
- **Alterações de campo serializado** (rename/remoção) em um componente já
  presente na cena salva: o Unity aplica o valor default do C# para o campo
  novo (comportamento normal, não é bug) — mas MUDAR nomes de campo depois de
  já ter cena salva pode causar comportamento inesperado até o próximo save;
  sempre recompilar + checar console + testar antes de salvar.
- **Sempre verificar o Console** (`read_console`) depois de compilar e depois
  de rodar em Play Mode — vários bugs reais desta sessão (ex.: renderer
  reabilitando toda hora, `IndexOutOfRangeException` num array) só apareceram
  assim, não em teste estático.
- Documentação médica extensa vive em `Docs/ClinicalModel.md` — **leia antes**
  de mexer em qualquer fórmula clínica; ele tem o histórico completo de por
  que cada constante é o que é.

## Itens em aberto / possíveis próximos passos

(Nenhum destes foi pedido ainda — só um mapa do que existe pendente.)

- Validar clinicamente os ~20 parâmetros arbitrários catalogados em
  `Docs/ClinicalModel.md` (checklist final).
- Adicionar mais doenças/condições (arquitetura já suporta múltiplos
  `DiseaseProcess`, nunca testado com mais de um simultâneo).
- Interface médica real (a atual é só debug).
- Sistema de redosagem/infusão contínua mais realista (hoje é "dose única"
  por clique; a bateria de validação mostrou que tratamento sem redosagem
  periódica falha a longo prazo — que é o comportamento pretendido, mas o
  jogo ainda não tem um jeito elegante de redosar/titular continuamente).
- `ThresholdCrossedEvent` de "choque séptico" ainda não usa
  `EffectivePerfusion` nem tem histerese (só a consciência foi corrigida na
  Revisão 4).
- Múltiplos pacientes / triagem / fluxo de pacientes pela UPA.
- Decoração e assets visuais da UPA (tudo é graybox hoje).

# Issues de desenvolvimento – MAC0693
Geradas a partir do deck de design (Figma). A #1 já foi criada no GitHub.

---

## 1. [Navegação] Teleporte com joystick direito e indicador de poste

**Design:** slide 5 (Storyboard – Navegação: Teleporte)

Com o **joystick do controle direito**, o usuário se move pela planta por teleporte instantâneo. Enquanto o joystick está acionado, um **poste** aparece no ponto de destino; o teleporte acontece **ao soltar o joystick**.

### Tarefas
- [ ] Verificar/configurar o `Teleportation Provider` do XR Origin para usar o joystick direito (XRI Starter Assets)
- [ ] Criar o visual de "poste" (cilindro vertical) e usá-lo como reticle do teleporte no lugar do padrão
- [ ] Definir áreas válidas de teleporte (chão da planta: `Floor` com `TeleportationArea`)
- [ ] Garantir que o teleporte não conflita com o laser de seleção do trigger direito (`User.cs`)

### Critérios de aceite
- Inclinar o joystick direito mostra o poste no chão apontado
- Soltar o joystick teleporta o usuário para o poste
- Não é possível teleportar para dentro de paredes ou fora da planta

---

## 2. [Navegação] World In Miniature (WIM) na mão esquerda

**Design:** slide 5 (Storyboard – Navegação: World In Miniature) e slide 8 (mapa dos controles)

Com o **botão A do controle esquerdo**, o usuário ativa uma miniatura (WIM) da planta na mão esquerda. Na miniatura existe um **poste que representa a posição do usuário**; com raycast + **trigger do controle direito**, ele seleciona o poste e o arrasta para a posição desejada, sendo teleportado para o ponto correspondente na planta.

### Tarefas
- [ ] Gerar a miniatura da planta (cópia em escala reduzida, só malhas, sem colliders de interação) presa ao controle esquerdo
- [ ] Mostrar/ocultar a WIM com o botão primário do controle esquerdo
- [ ] Representar a posição/orientação atual do usuário com um poste na miniatura, atualizado em tempo real
- [ ] Permitir selecionar e arrastar o poste com o laser do controle direito
- [ ] Ao soltar, converter a posição da miniatura para coordenadas do mundo e teleportar o usuário
- [ ] Atualizar a miniatura quando a planta for editada (escala de paredes, objetos movidos)

### Observação
O controle esquerdo do Vive Focus 3 tem os botões **X/Y** (não A/B). Mapear para o *primary button* do controle esquerdo no Input Action e atualizar o mapa de controles no design se necessário.

### Critérios de aceite
- Botão primário esquerdo alterna a WIM
- Arrastar o poste na WIM e soltar teleporta o usuário para o local equivalente

---

## 3. [Edição] Escala de objetos com trigger + movimento frente/trás

**Design:** slide 6 (Storyboard – Edição: Escala de objetos)

Com o **trigger do controle direito**, o usuário seleciona via raycast um objeto (ex.: janela) e altera sua escala ao **mover o controle para frente e para trás**.

### Situação atual
`User.cs` já faz isso para divisórias (`ScaleWall`, eixo Z, limites via `WallProperties`). Para objetos com tag `Moveable`, o trigger hoje **move** o objeto pelo chão (e gira com o pulso), o que não está no design.

### Tarefas
- [ ] Decidir a interação: quais objetos escalam e quais se movem (ex.: nova tag/componente `Scalable`, ou modo de edição alternável)
- [ ] Implementar escala uniforme do objeto selecionado a partir do deslocamento do controle no eixo forward
- [ ] Limites mínimo/máximo por objeto (componente semelhante a `WallProperties`)
- [ ] Atualizar o design caso mover/girar móveis continue no app

### Critérios de aceite
- Trigger em um objeto escalável + mover o controle para frente/trás muda a escala dentro dos limites
- Soltar o trigger mantém a escala final

---

## 4. [Edição] Feedback de seleção com emissão de luz

**Design:** slides 6 e 7 – "Como feedback, o objeto/divisória selecionado possui uma pequena emissão de luz."

### Situação atual
`User.cs` aplica um *tint* de cor no `material.color` (`objectHoverTint` / `objectHighlightTint`), o que também cria instâncias de material em runtime.

### Tarefas
- [ ] Trocar o tint por emissão (`_EmissionColor` + keyword `_EMISSION` no URP Lit), ou por um shader/material de highlight
- [ ] Hover (laser apontando) e seleção (trigger apertado) com intensidades diferentes
- [ ] Restaurar o material original ao soltar, inclusive para objetos com vários renderers/filhos
- [ ] Preferir `MaterialPropertyBlock` para não instanciar materiais

### Critérios de aceite
- Objetos e divisórias apontados/selecionados "brilham" levemente e voltam ao normal ao soltar
- Funciona em objetos com múltiplos materiais e em filhos

---

## 5. [UI] Componente base de janela/painel em VR

**Design:** slides 7 e 8 (janelas de materiais, objetos e projetos)

Várias funcionalidades usam uma janela flutuante com grade de opções selecionáveis por raycast + trigger direito. Criar um componente reutilizável antes das telas específicas.

### Tarefas
- [ ] Prefab de painel world-space (Canvas + `TrackedDeviceGraphicRaycaster`) com título e grade de itens (ícone + nome)
- [ ] Interação com o laser do controle direito (XRI UI / `XRRayInteractor`), compatível com o laser de `User.cs`
- [ ] Abrir o painel na frente do usuário/perto do objeto alvo, virado para a câmera
- [ ] Fechar ao selecionar um item ou ao apertar o grab de novo
- [ ] Rolagem quando houver muitos itens (barra lateral do wireframe)

### Critérios de aceite
- Um painel pode ser aberto com uma lista de itens e devolve o item escolhido por evento/callback

---

## 6. [UI] Tela inicial de seleção de plantas

**Design:** slide 8 (wireframe 1.1 – Selecionar planta) e slide 9 (demo: tela inicial de seleção de plantas)

### Tarefas
- [ ] Cena/estado inicial com o painel "Projetos" listando as plantas disponíveis (miniatura + nome)
- [ ] Ao escolher, carregar a planta correspondente e posicionar o usuário no ponto inicial
- [ ] Opção de voltar à seleção de plantas durante o uso
- [ ] Gerar miniaturas das plantas (render ou imagem estática)

### Depende de
- Componente base de janela/painel em VR
- Pipeline de importação de plantas do CAD

### Critérios de aceite
- Ao abrir o app, o usuário vê a lista de plantas e consegue entrar em qualquer uma

---

## 7. [Edição] Janela de materiais de divisórias

**Design:** slide 7 (Storyboard – Edição: Material de divisórias) e slide 8 (wireframe 1.2 – Selecionar material)

Com o **grab do controle direito** apontando para uma divisória, abre a janela de seleção de materiais. Com raycast + **trigger direito**, o usuário escolhe o material, que é aplicado à divisória.

### Tarefas
- [ ] Biblioteca de materiais (ScriptableObject com material URP + ícone + nome)
- [ ] Detectar o grab direito com o laser sobre um objeto com tag `Wall` e abrir o painel de materiais
- [ ] Aplicar o material escolhido apenas à divisória selecionada
- [ ] Definir se a mudança vale para a face/segmento ou para a parede inteira (a malha importada agrupa várias paredes em um objeto)

### Depende de
- Componente base de janela/painel em VR

### Critérios de aceite
- Grab em uma divisória abre o painel; escolher um material troca a aparência daquela divisória

---

## 8. [Edição] Adição de objetos (janela, mesa, porta…)

**Design:** slide 7 (Storyboard – Edição: Adição de objetos) e slide 8 (wireframes 2.1 – Selecionar tipo de objeto e 2.2 – Customizar objeto)

Com o **grab do controle direito**, abre a janela de seleção de objetos. Fluxo em dois passos: escolher o **tipo** (Janela, Mesa, Porta…) e depois o **modelo** específico. O objeto escolhido é colocado no ambiente (ex.: janela na parede apontada).

### Tarefas
- [ ] Catálogo de objetos por categoria (ScriptableObject com prefab + ícone)
- [ ] Painel em dois níveis: tipos → modelos
- [ ] Posicionamento: janelas/portas encaixam na parede apontada; móveis vão para o chão
- [ ] Objeto adicionado já entra com a tag `Moveable` (ou a definida na issue de escala) e com colliders
- [ ] Definir como diferenciar o grab de "materiais" (apontando para divisória) do grab de "adicionar objeto" (ex.: apontando para o vazio/chão)

### Depende de
- Componente base de janela/painel em VR

### Critérios de aceite
- O usuário escolhe tipo e modelo e o objeto aparece no local apontado, podendo ser editado depois

---

## 9. [Infra] Pipeline de importação de plantas do CAD (.fbx/.stl)

**Design:** slide 9 (demo: modelo exemplo de uma casa direto do CAD)

Hoje a planta (`apartment_3d_floor_plan.fbx`) foi configurada manualmente: tags `Wall`/`Moveable`, colliders e componentes. Para suportar várias plantas, isso precisa ser repetível.

### Tarefas
- [ ] Definir convenção de nomes/hierarquia no CAD (ex.: `Walls/`, `Columns/`, `Floor`, `Móveis/`)
- [ ] Script de editor (AssetPostprocessor ou botão de menu) que aplica tags, `MeshCollider`/`BoxCollider`, `WallProperties` e `TeleportationArea` no chão
- [ ] Corrigir escala/eixo (o modelo atual usa Z para cima e escala 100)
- [ ] Testar com um `.stl` além do `.fbx`
- [ ] Ajustar pivôs dos móveis (hoje alguns não ficam no centro; a rotação já compensa isso em `User.cs`)

### Critérios de aceite
- Importar uma nova planta e rodar a ferramenta deixa ela pronta para navegação e edição sem configuração manual

---

## 10. [Infra] Mapeamento de controles para o Vive Focus

**Design:** slide 8 (Mapa dos controles)

| Controle | Botão | Ação |
|---|---|---|
| Direito | Trigger | Navegar via WIM, edição de escala, selecionar nas janelas |
| Direito | Joystick | Navegação por teleporte |
| Direito | Grab | Edição de materiais / abrir janela |
| Esquerdo | A (primário) | Abrir WIM |

### Tarefas
- [ ] Centralizar as ações em um Input Action Asset do projeto (hoje `User.cs` recebe só o trigger direito)
- [ ] Adicionar o interaction profile *VIVE Focus 3 Controller* e validar todos os bindings no headset
- [ ] Remover/desativar ações do template que conflitem (ex.: snap turn, grab do template)
- [ ] (Opcional) Dicas visuais nos controles com os callouts do VR Template

### Critérios de aceite
- Todas as ações do mapa funcionam no Vive Focus com os botões indicados

---

## 11. [Infra] Documentar e estabilizar o build Android para Vive Focus

O build para o Vive Focus já funciona, mas exigiu ajustes que precisam ficar registrados no repositório.

### Ajustes já feitos
- VIVE OpenXR Plugin instalado pelo registry `https://npm-registry.vive.com` (scope `com.htc.upm`)
- OpenXR (Android) com o feature group **VIVE XR Support**
- `Assets/Plugins/Android/LauncherManifest.xml` com `tools:replace="android:authorities"` no `<provider>` do OpenXR (conflito entre o loader da VIVE e o do Unity OpenXR)
- **Minimum API Level = 32** (o Vive Focus roda Android 12L; o pacote Android XR tinha subido para 34)

### Tarefas
- [ ] Remover os pacotes `com.unity.xr.meta-openxr` e `com.unity.xr.androidxr-openxr`, que não são usados e forçam configurações incompatíveis
- [ ] Criar um Build Profile "Vive Focus" com as configurações acima
- [ ] README com o passo a passo: build, `adb install -r`, modo desenvolvedor no headset
- [ ] Verificar hand tracking com XR Hands 1.9 no Vive (o plugin da VIVE historicamente pedia XR Hands 1.3)

### Critérios de aceite
- Qualquer membro da equipe consegue gerar e instalar o APK seguindo o README

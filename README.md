# Proyecto_SolParaTodos_DA

📌 Sobre el Proyecto

"Sol Para Todos" es una aplicación interactiva desarrollada en Unity 6, concebida bajo el marco de una ONG ficticia de energías renovables. Su propósito principal es reducir la brecha energética y tecnológica en comunidades rurales vulnerables de la península de Yucatán.

A través de una experiencia inmersiva en 3D y Realidad Aumentada (AR), la herramienta busca:

Fomentar el ahorro económico familiar al desmitificar el uso y funcionamiento de la energía solar fotovoltaica.
Capacitar a técnicos instaladores locales mediante despieces anatómicos de hardware sin riesgo de dañar equipo real.
Impulsar el empleo verde autónomo dentro de las propias comunidades.

✨ Funcionalidades Principales
1. 🌅 Simulador de Trayectoria y Horario Solar
Dial Interactivo (6:00 AM – 6:00 PM): Permite simular el recorrido del Sol desde el amanecer (Este), pasando por el cenit a la 1:00 PM, hasta el atardecer (Oeste).
Sombras y Temperatura de Color Dinámicas: Visualiza en tiempo real cómo la inclinación solar y el sombreado afectan directamente la eficiencia energética del panel.

2. 🧩 Modo de Inspección Anatómica por Capas (Despiece 3D)
Control Gradual de Despiece (0% a 100%): Separa perpendicularmente las capas físicas del panel fotovoltaico:
Malla Metálica (Protección superior).
Panel de Cristal Templado (Vidrio de alta transmitancia con shader optimizado contra overdraw).
Celda Solar Captadora (Obleas de silicio fotovoltaico).
Base / Chasis Estructural.
Etiquetas AR Flotantes: Marcadores informativos en pantalla que siguen la posición tridimensional de cada capa despiezada.
Órbita 3D Libre: Permite girar alrededor del panel desmontado para examinarlo desde cualquier ángulo.


3. ⚡ Mapeo del Sistema Eléctrico Completo
Visualización de Flujo de Corriente: Un interruptor maestro activa pulsos lumínicos tridimensionales y texturas animadas (UV Scrolling) a lo largo del cableado.
Ruta de Transformación Energética:
- Mapea el circuito completo desde la Captación (Paneles en DC)
- Conversión (Inversor Solar a AC) 
- Almacenamiento (Banco de Baterías de 12V) 
- Consumo (Vivienda rural).


4. 📱 Introducción Guiada y Diseño Accesible (UX/UI)
Secuencia en 2 Etapas:
Splash Institucional con marco perimetral que simula una celda solar cargándose progresivamente de luz (0% → 100%).
Pantalla de Objetivos y Contexto Comunitario con estética Blueprint fotovoltaico.
Matriz de Accesibilidad: Botones de alto contraste táctil, navegación suave para prevenir fatiga visual y adaptación automática a pantallas móviles con muescas (Safe Area).


🛠️ Tecnologías y Herramientas
Motor de Render: Unity 6 (6000.5.7f1)
Pipeline Gráfico: Universal Render Pipeline (URP)
Modelado y Texturizado 3D: Blender
Sistema de Entrada: Unity Input System Package (Soporte simultáneo Touch / Mouse / Teclado)
Lenguaje: C# (.NET Standard)
Plataforma Objetivo: Android (Optimizado para 60 FPS estables en gama media-baja)
⚙️ Optimización para Dispositivos Móviles
Bajo Draw Call y Overdraw: Cero asignaciones de memoria por frame (0 GC Alloc) en animaciones de flujo y paneles.
Sombreado Equilibrado: Configuración URP de una sola cascada con distancia de sombras acotada <30m) para evitar sobrecalentamiento en baterías móviles.
Materiales Transparentes Eficientes: Supresión de emisión de sombras en el cristal del panel para mitigar el costo del canal alfa en GPUs móviles (Mali / Adreno).



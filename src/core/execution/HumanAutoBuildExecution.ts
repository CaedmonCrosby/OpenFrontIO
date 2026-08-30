import { Execution, Game, Player } from "../game/Game";
import { PseudoRandom } from "../PseudoRandom";
import { simpleHash } from "../Util";
import { NationEmojiBehavior } from "./nation/NationEmojiBehavior";
import { NationStructureBehavior } from "./nation/NationStructureBehavior";
import { NationWarshipBehavior } from "./nation/NationWarshipBehavior";

/**
 * Builds City, Factory, Port, and SAM (and rarely Warship) for the human
 * player using the nation placement brain. One action per pulse; stacking
 * is upgrading an existing well-chosen structure of the same type.
 */
export class HumanAutoBuildExecution implements Execution {
  private active = true;
  private mg: Game | null = null;
  private random: PseudoRandom | null = null;
  private structureBehavior: NationStructureBehavior | null = null;
  private warshipBehavior: NationWarshipBehavior | null = null;
  private inited = false;
  private readonly pulseTicks = 20;

  constructor(private readonly player: Player) {}

  init(mg: Game, _ticks: number): void {
    this.mg = mg;
    this.random = new PseudoRandom(simpleHash(this.player.id()) + 17);
  }

  tick(ticks: number): void {
    if (!this.player.isAlive()) {
      this.active = false;
      return;
    }
    const mg = this.mg;
    const random = this.random;
    if (mg === null || random === null) return;
    if (mg.inSpawnPhase() || !this.player.hasSpawned()) return;

    if (!this.inited) {
      const emoji = new NationEmojiBehavior(random, mg, this.player);
      this.structureBehavior = new NationStructureBehavior(
        random,
        mg,
        this.player,
        true,
      );
      this.warshipBehavior = new NationWarshipBehavior(
        random,
        mg,
        this.player,
        emoji,
      );
      this.inited = true;
    }

    if (ticks % this.pulseTicks !== 0) return;

    this.structureBehavior?.handleStructures();
    // Warships on a slower pulse so they stay uncommon.
    if (ticks % (this.pulseTicks * 5) === 0) {
      this.warshipBehavior?.maybeSpawnWarship();
    }
  }

  isActive(): boolean {
    return this.active;
  }

  activeDuringSpawnPhase(): boolean {
    return false;
  }
}

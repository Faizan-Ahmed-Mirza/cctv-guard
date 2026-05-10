import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

/**
 * Singleton service that tracks the unread alert count across the app.
 * The header subscribes to count$ and displays the badge.
 * The alerts page calls reset() when visited so the badge clears immediately
 * without needing a page reload or API round-trip.
 */
@Injectable({ providedIn: 'root' })
export class AlertBadgeService {
  private readonly _count = new BehaviorSubject<number>(0);

  /** Observable badge count — header subscribes to this. */
  readonly count$ = this._count.asObservable();

  /** Set the badge to a specific number (called after API load). */
  set(count: number): void {
    this._count.next(Math.max(0, count));
  }

  /** Increment by 1 — called when a new alert arrives via SignalR. */
  increment(): void {
    this._count.next(this._count.value + 1);
  }

  /** Reset to zero — called when the user visits the alerts page. */
  reset(): void {
    this._count.next(0);
  }

  get current(): number {
    return this._count.value;
  }
}

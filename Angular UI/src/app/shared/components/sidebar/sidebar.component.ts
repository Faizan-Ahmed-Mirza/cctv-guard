import { Component, Input, Output, EventEmitter, signal, OnInit, OnDestroy } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../../services/auth.service';
import { AlertBadgeService } from '../../../services/alert-badge.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss'
})
export class SidebarComponent implements OnInit, OnDestroy {
  @Input() collapsed  = false;
  @Input() mobileOpen = false;
  @Input() isMobile   = false;
  @Output() linkClicked = new EventEmitter<void>();

  unreadAlerts = signal(0);
  private badgeSub: Subscription | null = null;

  get navItems() {
    const role = this.auth.currentUser()?.role;
    const all = [
      { path: '/dashboard/monitor',       icon: '📹', label: 'Live Monitor',  key: 'monitor'   },
      { path: '/dashboard/incidents',     icon: '⚠️', label: 'Incidents',     key: 'incidents' },
      { path: '/dashboard/alerts',        icon: '🔔', label: 'Alerts',        key: 'alerts'    },
      { path: '/dashboard/analytics',     icon: '📊', label: 'Analytics',     key: 'analytics',   adminOnly: true  },
      { path: '/dashboard/configuration', icon: '⚙️', label: 'Configuration', key: 'config',      minOperator: true },
    ];
    return all.filter(item => {
      if (item.adminOnly)   return role === 'Admin';
      if (item.minOperator) return role === 'Admin' || role === 'Operator';
      return true;
    });
  }

  constructor(public auth: AuthService, private badge: AlertBadgeService) {}

  ngOnInit(): void {
    // Subscribe to the shared badge service — stays in sync with header badge
    this.badgeSub = this.badge.count$.subscribe(n => this.unreadAlerts.set(n));
  }

  ngOnDestroy(): void {
    this.badgeSub?.unsubscribe();
  }

  onNavClick(): void { this.linkClicked.emit(); }
}

import { Component, Input, Output, EventEmitter, signal, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../../services/auth.service';
import { ApiService } from '../../../services/api.service';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss'
})
export class SidebarComponent implements OnInit {
  @Input() collapsed  = false;
  @Input() mobileOpen = false;
  @Input() isMobile   = false;
  @Output() linkClicked = new EventEmitter<void>();

  unreadAlerts = signal(0);

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

  constructor(public auth: AuthService, private api: ApiService) {}

  async ngOnInit(): Promise<void> {
    await this.loadUnreadCount();
    setInterval(() => this.loadUnreadCount(), 30000);
  }

  private async loadUnreadCount(): Promise<void> {
    try {
      const alerts = await firstValueFrom(this.api.getAlerts(undefined, false));
      this.unreadAlerts.set(alerts.filter(a => !a.read).length);
    } catch { /* ignore */ }
  }

  onNavClick(): void { this.linkClicked.emit(); }
}

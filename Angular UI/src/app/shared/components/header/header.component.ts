import { Component, signal, OnInit, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../services/auth.service';
import { ApiService } from '../../../services/api.service';
import { ThemeService } from '../../../services/theme.service';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './header.component.html',
  styleUrl: './header.component.scss'
})
export class HeaderComponent implements OnInit {
  @Output() toggleSidebar = new EventEmitter<void>();

  unreadAlerts = signal(0);
  currentTime  = new Date();

  constructor(
    public auth: AuthService,
    public theme: ThemeService,
    private api: ApiService
  ) {
    setInterval(() => this.currentTime = new Date(), 1000);
    setInterval(() => this.loadUnreadCount(), 30000);
  }

  async ngOnInit(): Promise<void> {
    await this.loadUnreadCount();
  }

  private async loadUnreadCount(): Promise<void> {
    try {
      const alerts = await firstValueFrom(this.api.getAlerts(undefined, false));
      this.unreadAlerts.set(alerts.filter(a => !a.read).length);
    } catch { /* ignore */ }
  }
}

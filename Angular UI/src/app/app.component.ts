import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './services/theme.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  // Injecting ThemeService here ensures it is instantiated (and the effect runs)
  // as soon as the app boots, before any child component renders.
  constructor(private _theme: ThemeService) {}
}
